﻿using DocumentFormat.OpenXml.Packaging;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using System.Xml.Schema;
using System.Xml.XPath;

namespace Codeuctivity.OpenXmlPowerTools
{
    public partial class DocumentAssembler
    {
        public static WmlDocument AssembleDocument(WmlDocument templateDoc, XmlDocument data, out bool templateError)
        {
            var xDoc = data.GetXDocument();
            return AssembleDocument(templateDoc, xDoc.Root, out templateError);
        }

        public static WmlDocument AssembleDocument(WmlDocument templateDoc, XElement data, out bool templateError)
        {
            var byteArray = templateDoc.DocumentByteArray;
            using var mem = new MemoryStream();
            mem.Write(byteArray, 0, byteArray.Length);
            using (var wordDoc = WordprocessingDocument.Open(mem, true))
            {
                if (RevisionAccepter.HasTrackedRevisions(wordDoc))
                {
                    throw new OpenXmlPowerToolsException("Invalid DocumentAssembler template - contains tracked revisions");
                }

                var te = new TemplateError();
                foreach (var part in wordDoc.ContentParts())
                {
                    ProcessTemplatePart(data, te, part);
                }
                templateError = te.HasError;
            }
            var assembledDocument = new WmlDocument("TempFileName.docx", mem.ToArray());
            return assembledDocument;
        }

        private static void ProcessTemplatePart(XElement data, TemplateError te, OpenXmlPart part)
        {
            var xDoc = part.GetXDocument();

            var xDocRoot = RemoveGoBackBookmarks(xDoc.Root);

            // content controls in cells can surround the W.tc element, so transform so that such content controls are within the cell content
            xDocRoot = (XElement)NormalizeContentControlsInCells(xDocRoot);

            xDocRoot = (XElement)TransformToMetadata(xDocRoot, data, te);

            // Table might have been placed at run-level, when it should be at block-level, so fix this.
            // Repeat, EndRepeat, Conditional, EndConditional are allowed at run level, but only if there is a matching pair
            // if there is only one Repeat, EndRepeat, Conditional, EndConditional, then move to block level.
            // if there is a matching pair, then is OK.
            xDocRoot = (XElement)ForceBlockLevelAsAppropriate(xDocRoot, te);

            NormalizeTablesRepeatAndConditional(xDocRoot, te);

            // any EndRepeat, EndConditional that remain are orphans, so replace with an error
            ProcessOrphanEndRepeatEndConditional(xDocRoot, te);

            // do the actual content replacement
            xDocRoot = ContentReplacementTransform(xDocRoot, data, te) as XElement;

            xDoc.Elements().First().ReplaceWith(xDocRoot);
            part.PutXDocument();
            return;
        }

        private static readonly XName[] s_MetaToForceToBlock = new XName[] {
            PA.Conditional,
            PA.EndConditional,
            PA.Repeat,
            PA.EndRepeat,
            PA.Table,
        };

        private static object ForceBlockLevelAsAppropriate(XNode node, TemplateError te)
        {
            if (node is XElement element)
            {
                if (element.Name == W.p)
                {
                    var childMeta = element.Elements().Where(n => s_MetaToForceToBlock.Contains(n.Name)).ToList();
                    if (childMeta.Count == 1)
                    {
                        var child = childMeta.First();
                        var otherTextInParagraph = element.Elements(W.r).Elements(W.t).Select(t => (string)t).StringConcatenate().Trim();
                        if (otherTextInParagraph != "")
                        {
                            var newPara = new XElement(element);
                            var newMeta = newPara.Elements().First(n => s_MetaToForceToBlock.Contains(n.Name));
                            newMeta.ReplaceWith(CreateRunErrorMessage("Error: Unmatched metadata can't be in paragraph with other text", te));
                            return newPara;
                        }
                        var meta = new XElement(child.Name,
                            child.Attributes(),
                            new XElement(W.p,
                                element.Attributes(),
                                element.Elements(W.pPr),
                                child.Elements()));
                        return meta;
                    }
                    var count = childMeta.Count;
                    if (count % 2 == 0)
                    {
                        if (childMeta.Where(c => c.Name == PA.Repeat).Count() != childMeta.Where(c => c.Name == PA.EndRepeat).Count())
                        {
                            return CreateContextErrorMessage(element, "Error: Mismatch Repeat / EndRepeat at run level", te);
                        }

                        if (childMeta.Where(c => c.Name == PA.Conditional).Count() != childMeta.Where(c => c.Name == PA.EndConditional).Count())
                        {
                            return CreateContextErrorMessage(element, "Error: Mismatch Conditional / EndConditional at run level", te);
                        }

                        return new XElement(element.Name,
                            element.Attributes(),
                            element.Nodes().Select(n => ForceBlockLevelAsAppropriate(n, te)));
                    }
                    else
                    {
                        return CreateContextErrorMessage(element, "Error: Invalid metadata at run level", te);
                    }
                }
                return new XElement(element.Name,
                    element.Attributes(),
                    element.Nodes().Select(n => ForceBlockLevelAsAppropriate(n, te)));
            }
            return node;
        }

        private static void ProcessOrphanEndRepeatEndConditional(XElement xDocRoot, TemplateError te)
        {
            foreach (var element in xDocRoot.Descendants(PA.EndRepeat).ToList())
            {
                var error = CreateContextErrorMessage(element, "Error: EndRepeat without matching Repeat", te);
                element.ReplaceWith(error);
            }
            foreach (var element in xDocRoot.Descendants(PA.EndConditional).ToList())
            {
                var error = CreateContextErrorMessage(element, "Error: EndConditional without matching Conditional", te);
                element.ReplaceWith(error);
            }
        }

        private static XElement RemoveGoBackBookmarks(XElement xElement)
        {
            var cloneXDoc = new XElement(xElement);
            while (true)
            {
                var bm = cloneXDoc.DescendantsAndSelf(W.bookmarkStart).FirstOrDefault(b => (string)b.Attribute(W.name) == "_GoBack");
                if (bm == null)
                {
                    break;
                }

                var id = (string)bm.Attribute(W.id);
                var endBm = cloneXDoc.DescendantsAndSelf(W.bookmarkEnd).FirstOrDefault(b => (string)b.Attribute(W.id) == id);
                bm.Remove();
                endBm.Remove();
            }
            return cloneXDoc;
        }

        // this transform inverts content controls that surround W.tc elements.  After transforming, the W.tc will contain
        // the content control, which contains the paragraph content of the cell.
        private static object NormalizeContentControlsInCells(XNode node)
        {
            if (node is XElement element)
            {
                if (element.Name == W.sdt && element.Parent.Name == W.tr)
                {
                    var newCell = new XElement(W.tc,
                        element.Elements(W.tc).Elements(W.tcPr),
                        new XElement(W.sdt,
                            element.Elements(W.sdtPr),
                            element.Elements(W.sdtEndPr),
                            new XElement(W.sdtContent,
                                element.Elements(W.sdtContent).Elements(W.tc).Elements().Where(e => e.Name != W.tcPr))));
                    return newCell;
                }
                return new XElement(element.Name,
                    element.Attributes(),
                    element.Nodes().Select(n => NormalizeContentControlsInCells(n)));
            }
            return node;
        }

        // The following method is written using tree modification, not RPFT, because it is easier to write in this fashion.
        // These types of operations are not as easy to write using RPFT.
        // Unless you are completely clear on the semantics of LINQ to XML DML, do not make modifications to this method.
        private static void NormalizeTablesRepeatAndConditional(XElement xDoc, TemplateError te)
        {
            var tables = xDoc.Descendants(PA.Table).ToList();
            foreach (var table in tables)
            {
                var followingElement = table.ElementsAfterSelf().FirstOrDefault(e => e.Name == W.tbl || e.Name == W.p);
                if (followingElement == null || followingElement.Name != W.tbl)
                {
                    table.ReplaceWith(CreateParaErrorMessage("Table metadata is not immediately followed by a table", te));
                    continue;
                }
                // remove superflous paragraph from Table metadata
                table.RemoveNodes();
                // detach w:tbl from parent, and add to Table metadata
                followingElement.Remove();
                table.Add(followingElement);
            }

            var repeatDepth = 0;
            var conditionalDepth = 0;
            foreach (var metadata in xDoc.Descendants().Where(d =>
                    d.Name == PA.Repeat ||
                    d.Name == PA.Conditional ||
                    d.Name == PA.EndRepeat ||
                    d.Name == PA.EndConditional))
            {
                if (metadata.Name == PA.Repeat)
                {
                    ++repeatDepth;
                    metadata.Add(new XAttribute(PA.Depth, repeatDepth));
                    continue;
                }
                if (metadata.Name == PA.EndRepeat)
                {
                    metadata.Add(new XAttribute(PA.Depth, repeatDepth));
                    --repeatDepth;
                    continue;
                }
                if (metadata.Name == PA.Conditional)
                {
                    ++conditionalDepth;
                    metadata.Add(new XAttribute(PA.Depth, conditionalDepth));
                    continue;
                }
                if (metadata.Name == PA.EndConditional)
                {
                    metadata.Add(new XAttribute(PA.Depth, conditionalDepth));
                    --conditionalDepth;
                    continue;
                }
            }

            while (true)
            {
                var didReplace = false;
                foreach (var metadata in xDoc.Descendants().Where(d => (d.Name == PA.Repeat || d.Name == PA.Conditional) && d.Attribute(PA.Depth) != null).ToList())
                {
                    var depth = (int)metadata.Attribute(PA.Depth);
                    XName? matchingEndName = null;
                    if (metadata.Name == PA.Repeat)
                    {
                        matchingEndName = PA.EndRepeat;
                    }
                    else if (metadata.Name == PA.Conditional)
                    {
                        matchingEndName = PA.EndConditional;
                    }

                    if (matchingEndName == null)
                    {
                        throw new OpenXmlPowerToolsException("Internal error");
                    }

                    var matchingEnd = metadata.ElementsAfterSelf(matchingEndName).FirstOrDefault(end => { return (int)end.Attribute(PA.Depth) == depth; });
                    if (matchingEnd == null)
                    {
                        metadata.ReplaceWith(CreateParaErrorMessage(string.Format("{0} does not have matching {1}", metadata.Name.LocalName, matchingEndName.LocalName), te));
                        continue;
                    }
                    metadata.RemoveNodes();
                    var contentBetween = metadata.ElementsAfterSelf().TakeWhile(after => after != matchingEnd).ToList();
                    foreach (var item in contentBetween)
                    {
                        item.Remove();
                    }

                    contentBetween = contentBetween.Where(n => n.Name != W.bookmarkStart && n.Name != W.bookmarkEnd).ToList();
                    metadata.Add(contentBetween);
                    metadata.Attributes(PA.Depth).Remove();
                    matchingEnd.Remove();
                    didReplace = true;
                    break;
                }
                if (!didReplace)
                {
                    break;
                }
            }
        }

        private static readonly List<string> s_AliasList = new List<string>()
        {
            "Content",
            "Table",
            "Repeat",
            "EndRepeat",
            "Conditional",
            "EndConditional",
        };

        private static object TransformToMetadata(XNode node, XElement data, TemplateError te)
        {
            if (node is XElement element)
            {
                if (element.Name == W.sdt)
                {
                    var alias = (string)element.Elements(W.sdtPr).Elements(W.alias).Attributes(W.val).FirstOrDefault();
                    if (alias == null || alias == "" || s_AliasList.Contains(alias))
                    {
                        var ccContents = element
                            .DescendantsTrimmed(W.txbxContent)
                            .Where(e => e.Name == W.t)
                            .Select(t => (string)t)
                            .StringConcatenate()
                            .Trim()
                            .Replace('“', '"')
                            .Replace('”', '"');
                        if (ccContents.StartsWith("<"))
                        {
                            var xml = TransformXmlTextToMetadata(te, ccContents);
                            if (xml.Name == W.p || xml.Name == W.r)  // this means there was an error processing the XML.
                            {
                                if (element.Parent.Name == W.p)
                                {
                                    return xml.Elements(W.r);
                                }

                                return xml;
                            }
                            if (alias != null && xml.Name.LocalName != alias)
                            {
                                if (element.Parent.Name == W.p)
                                {
                                    return CreateRunErrorMessage("Error: Content control alias does not match metadata element name", te);
                                }
                                else
                                {
                                    return CreateParaErrorMessage("Error: Content control alias does not match metadata element name", te);
                                }
                            }
                            xml.Add(element.Elements(W.sdtContent).Elements());
                            return xml;
                        }
                        return new XElement(element.Name,
                            element.Attributes(),
                            element.Nodes().Select(n => TransformToMetadata(n, data, te)));
                    }
                    return new XElement(element.Name,
                        element.Attributes(),
                        element.Nodes().Select(n => TransformToMetadata(n, data, te)));
                }
                if (element.Name == W.p)
                {
                    var paraContents = element
                        .DescendantsTrimmed(W.txbxContent)
                        .Where(e => e.Name == W.t)
                        .Select(t => (string)t)
                        .StringConcatenate()
                        .Trim();
                    var occurances = paraContents.Select((c, i) => paraContents.Substring(i)).Count(sub => sub.StartsWith("<#"));
                    if (paraContents.StartsWith("<#") && paraContents.EndsWith("#>") && occurances == 1)
                    {
                        var xmlText = paraContents.Substring(2, paraContents.Length - 4).Trim();
                        var xml = TransformXmlTextToMetadata(te, xmlText);
                        if (xml.Name == W.p || xml.Name == W.r)
                        {
                            return xml;
                        }

                        xml.Add(element);
                        return xml;
                    }
                    if (paraContents.Contains("<#"))
                    {
                        var runReplacementInfo = new List<RunReplacementInfo>();
                        var thisGuid = Guid.NewGuid().ToString();
                        var r = new Regex("<#.*?#>");
                        XElement? xml = null;
                        OpenXmlRegex.Replace(new[] { element }, r, thisGuid, (para, match) =>
                        {
                            var matchString = match.Value.Trim();
                            var xmlText = matchString.Substring(2, matchString.Length - 4).Trim().Replace('“', '"').Replace('”', '"');
                            try
                            {
                                xml = XElement.Parse(xmlText);
                            }
                            catch (XmlException e)
                            {
                                var rri = new RunReplacementInfo()
                                {
                                    Xml = null,
                                    XmlExceptionMessage = "XmlException: " + e.Message,
                                    SchemaValidationMessage = null,
                                };
                                runReplacementInfo.Add(rri);
                                return true;
                            }
                            var schemaError = ValidatePerSchema(xml);
                            if (schemaError != null)
                            {
                                var rri = new RunReplacementInfo()
                                {
                                    Xml = null,
                                    XmlExceptionMessage = null,
                                    SchemaValidationMessage = "Schema Validation Error: " + schemaError,
                                };
                                runReplacementInfo.Add(rri);
                                return true;
                            }
                            var rri2 = new RunReplacementInfo()
                            {
                                Xml = xml,
                                XmlExceptionMessage = null,
                                SchemaValidationMessage = null,
                            };
                            runReplacementInfo.Add(rri2);
                            return true;
                        }, false);

                        var newPara = new XElement(element);
                        foreach (var rri in runReplacementInfo)
                        {
                            var runToReplace = newPara.Descendants(W.r).FirstOrDefault(rn => rn.Value == thisGuid && rn.Parent.Name != PA.Content);
                            if (runToReplace == null)
                            {
                                throw new OpenXmlPowerToolsException("Internal error");
                            }

                            if (rri.XmlExceptionMessage != null)
                            {
                                runToReplace.ReplaceWith(CreateRunErrorMessage(rri.XmlExceptionMessage, te));
                            }
                            else if (rri.SchemaValidationMessage != null)
                            {
                                runToReplace.ReplaceWith(CreateRunErrorMessage(rri.SchemaValidationMessage, te));
                            }
                            else
                            {
                                var newXml = new XElement(rri.Xml);
                                newXml.Add(runToReplace);
                                runToReplace.ReplaceWith(newXml);
                            }
                        }
                        var coalescedParagraph = WordprocessingMLUtil.CoalesceAdjacentRunsWithIdenticalFormatting(newPara);
                        return coalescedParagraph;
                    }
                }

                return new XElement(element.Name,
                    element.Attributes(),
                    element.Nodes().Select(n => TransformToMetadata(n, data, te)));
            }
            return node;
        }

        private static XElement TransformXmlTextToMetadata(TemplateError te, string xmlText)
        {
            XElement xml;
            try
            {
                xml = XElement.Parse(xmlText);
            }
            catch (XmlException e)
            {
                return CreateParaErrorMessage("XmlException: " + e.Message, te);
            }
            var schemaError = ValidatePerSchema(xml);
            if (schemaError != null)
            {
                return CreateParaErrorMessage("Schema Validation Error: " + schemaError, te);
            }

            return xml;
        }

        private class RunReplacementInfo
        {
            public XElement? Xml;
            public string? XmlExceptionMessage;
            public string? SchemaValidationMessage;
        }

        private static string? ValidatePerSchema(XElement element)
        {
            if (s_PASchemaSets == null)
            {
                s_PASchemaSets = new Dictionary<XName, PASchemaSet>()
                {
                    {
                        PA.Content,
                        new PASchemaSet() {
                            XsdMarkup =
                              @"<xs:schema attributeFormDefault='unqualified' elementFormDefault='qualified' xmlns:xs='http://www.w3.org/2001/XMLSchema'>
                                  <xs:element name='Content'>
                                    <xs:complexType>
                                      <xs:attribute name='Select' type='xs:string' use='required' />
                                      <xs:attribute name='Optional' type='xs:boolean' use='optional' />
                                    </xs:complexType>
                                  </xs:element>
                                </xs:schema>",
                        }
                    },
                    {
                        PA.Table,
                        new PASchemaSet() {
                            XsdMarkup =
                              @"<xs:schema attributeFormDefault='unqualified' elementFormDefault='qualified' xmlns:xs='http://www.w3.org/2001/XMLSchema'>
                                  <xs:element name='Table'>
                                    <xs:complexType>
                                      <xs:attribute name='Select' type='xs:string' use='required' />
                                    </xs:complexType>
                                  </xs:element>
                                </xs:schema>",
                        }
                    },
                    {
                        PA.Repeat,
                        new PASchemaSet() {
                            XsdMarkup =
                              @"<xs:schema attributeFormDefault='unqualified' elementFormDefault='qualified' xmlns:xs='http://www.w3.org/2001/XMLSchema'>
                                  <xs:element name='Repeat'>
                                    <xs:complexType>
                                      <xs:attribute name='Select' type='xs:string' use='required' />
                                      <xs:attribute name='Optional' type='xs:boolean' use='optional' />
                                    </xs:complexType>
                                  </xs:element>
                                </xs:schema>",
                        }
                    },
                    {
                        PA.EndRepeat,
                        new PASchemaSet() {
                            XsdMarkup =
                              @"<xs:schema attributeFormDefault='unqualified' elementFormDefault='qualified' xmlns:xs='http://www.w3.org/2001/XMLSchema'>
                                  <xs:element name='EndRepeat' />
                                </xs:schema>",
                        }
                    },
                    {
                        PA.Conditional,
                        new PASchemaSet() {
                            XsdMarkup =
                              @"<xs:schema attributeFormDefault='unqualified' elementFormDefault='qualified' xmlns:xs='http://www.w3.org/2001/XMLSchema'>
                                  <xs:element name='Conditional'>
                                    <xs:complexType>
                                      <xs:attribute name='Select' type='xs:string' use='required' />
                                      <xs:attribute name='Match' type='xs:string' use='optional' />
                                      <xs:attribute name='NotMatch' type='xs:string' use='optional' />
                                    </xs:complexType>
                                  </xs:element>
                                </xs:schema>",
                        }
                    },
                    {
                        PA.EndConditional,
                        new PASchemaSet() {
                            XsdMarkup =
                              @"<xs:schema attributeFormDefault='unqualified' elementFormDefault='qualified' xmlns:xs='http://www.w3.org/2001/XMLSchema'>
                                  <xs:element name='EndConditional' />
                                </xs:schema>",
                        }
                    },
                };
                foreach (var item in s_PASchemaSets)
                {
                    var itemPAss = item.Value;
                    var schemas = new XmlSchemaSet();
                    schemas.Add("", XmlReader.Create(new StringReader(itemPAss.XsdMarkup)));
                    itemPAss.SchemaSet = schemas;
                }
            }
            if (!s_PASchemaSets.ContainsKey(element.Name))
            {
                return string.Format("Invalid XML: {0} is not a valid element", element.Name.LocalName);
            }
            var paSchemaSet = s_PASchemaSets[element.Name];
            var d = new XDocument(element);
            string? message = null;
            d.Validate(paSchemaSet.SchemaSet, (sender, e) =>
            {
                if (message == null)
                {
                    message = e.Message;
                }
            }, true);
            if (message != null)
            {
                return message;
            }

            return null;
        }

        private static Dictionary<XName, PASchemaSet> s_PASchemaSets;

        private static object? ContentReplacementTransform(XNode node, XElement data, TemplateError templateError)
        {
            if (node is XElement element)
            {
                if (element.Name == PA.Content)
                {
                    var para = element.Descendants(W.p).FirstOrDefault();
                    var run = element.Descendants(W.r).FirstOrDefault();

                    var xPath = (string)element.Attribute(PA.Select);
                    var optionalString = (string)element.Attribute(PA.Optional);
                    var optional = (optionalString != null && optionalString.ToLower() == "true");

                    string newValue;
                    try
                    {
                        newValue = EvaluateXPathToString(data, xPath, optional);
                    }
                    catch (XPathException e)
                    {
                        return CreateContextErrorMessage(element, "XPathException: " + e.Message, templateError);
                    }

                    if (para != null)
                    {
                        var p = new XElement(W.p, para.Elements(W.pPr));
                        foreach (var line in newValue.Split('\n'))
                        {
                            p.Add(new XElement(W.r,
                                    para.Elements(W.r).Elements(W.rPr).FirstOrDefault(),
                                (p.Elements().Count() > 1) ? new XElement(W.br) : null,
                                new XElement(W.t, line)));
                        }
                        return p;
                    }
                    else
                    {
                        var list = new List<XElement>();
                        foreach (var line in newValue.Split('\n'))
                        {
                            list.Add(new XElement(W.r,
                                run.Elements().Where(e => e.Name != W.t),
                                (list.Count > 0) ? new XElement(W.br) : null,
                                new XElement(W.t, line)));
                        }
                        return list;
                    }
                }
                if (element.Name == PA.Repeat)
                {
                    var selector = (string)element.Attribute(PA.Select);
                    var optionalString = (string)element.Attribute(PA.Optional);
                    var optional = (optionalString != null && optionalString.ToLower() == "true");

                    IEnumerable<XElement> repeatingData;
                    try
                    {
                        repeatingData = data.XPathSelectElements(selector);
                    }
                    catch (XPathException e)
                    {
                        return CreateContextErrorMessage(element, "XPathException: " + e.Message, templateError);
                    }
                    if (!repeatingData.Any())
                    {
                        if (optional)
                        {
                            return null;
                        }
                        return CreateContextErrorMessage(element, "Repeat: Select returned no data", templateError);
                    }
                    var newContent = repeatingData.Select(d =>
                        {
                            var content = element
                                .Elements()
                                .Select(e => ContentReplacementTransform(e, d, templateError))
                                .ToList();
                            return content;
                        })
                        .ToList();
                    return newContent;
                }
                if (element.Name == PA.Table)
                {
                    IEnumerable<XElement> tableData;
                    try
                    {
                        tableData = data.XPathSelectElements((string)element.Attribute(PA.Select));
                    }
                    catch (XPathException e)
                    {
                        return CreateContextErrorMessage(element, "XPathException: " + e.Message, templateError);
                    }
                    if (!tableData.Any())
                    {
                        return CreateContextErrorMessage(element, "Table Select returned no data", templateError);
                    }

                    var table = element.Element(W.tbl);
                    var protoRow = table.Elements(W.tr).Skip(1).FirstOrDefault();
                    var footerRowsBeforeTransform = table
                        .Elements(W.tr)
                        .Skip(2)
                        .ToList();
                    var footerRows = footerRowsBeforeTransform
                        .Select(x => ContentReplacementTransform(x, data, templateError))
                        .ToList();
                    if (protoRow == null)
                    {
                        return CreateContextErrorMessage(element, string.Format("Table does not contain a prototype row"), templateError);
                    }

                    protoRow.Descendants(W.bookmarkStart).Remove();
                    protoRow.Descendants(W.bookmarkEnd).Remove();
                    var newTable = new XElement(W.tbl,
                        table.Elements().Where(e => e.Name != W.tr),
                        table.Elements(W.tr).FirstOrDefault(),
                        tableData.Select(d =>
                            new XElement(W.tr,
                                protoRow.Elements().Where(r => r.Name != W.tc),
                                protoRow.Elements(W.tc)
                                    .Select(tc =>
                                    {
                                        var paragraph = tc.Elements(W.p).FirstOrDefault();
                                        var cellRun = paragraph.Elements(W.r).FirstOrDefault();
                                        var xPath = paragraph.Value;
                                        string? newValue = null;
                                        try
                                        {
                                            newValue = EvaluateXPathToString(d, xPath, false);
                                        }
                                        catch (XPathException e)
                                        {
                                            var errorCell = new XElement(W.tc,
                                                tc.Elements().Where(z => z.Name != W.p),
                                                new XElement(W.p,
                                                    paragraph.Element(W.pPr),
                                                    CreateRunErrorMessage(e.Message, templateError)));
                                            return errorCell;
                                        }

                                        var newCell = new XElement(W.tc,
                                                   tc.Elements().Where(z => z.Name != W.p),
                                                   new XElement(W.p,
                                                       paragraph.Element(W.pPr),
                                                       new XElement(W.r,
                                                           cellRun != null ? cellRun.Element(W.rPr) : new XElement(W.rPr),  //if the cell was empty there is no cellrun
                                                           new XElement(W.t, newValue))));
                                        return newCell;
                                    }))),
                                    footerRows
                                    );
                    return newTable;
                }
                if (element.Name == PA.Conditional)
                {
                    var xPath = (string)element.Attribute(PA.Select);
                    var match = (string)element.Attribute(PA.Match);
                    var notMatch = (string)element.Attribute(PA.NotMatch);

                    if (match == null && notMatch == null)
                    {
                        return CreateContextErrorMessage(element, "Conditional: Must specify either Match or NotMatch", templateError);
                    }

                    if (match != null && notMatch != null)
                    {
                        return CreateContextErrorMessage(element, "Conditional: Cannot specify both Match and NotMatch", templateError);
                    }

                    string? testValue = null;

                    try
                    {
                        testValue = EvaluateXPathToString(data, xPath, false);
                    }
                    catch (XPathException e)
                    {
                        return CreateContextErrorMessage(element, e.Message, templateError);
                    }

                    if ((match != null && testValue == match) || (notMatch != null && testValue != notMatch))
                    {
                        var content = element.Elements().Select(e => ContentReplacementTransform(e, data, templateError));
                        return content;
                    }
                    return null;
                }
                return new XElement(element.Name,
                    element.Attributes(),
                    element.Nodes().Select(n => ContentReplacementTransform(n, data, templateError)));
            }
            return node;
        }

        private static object CreateContextErrorMessage(XElement element, string errorMessage, TemplateError templateError)
        {
            var para = element.Descendants(W.p).FirstOrDefault();
            var errorRun = CreateRunErrorMessage(errorMessage, templateError);
            if (para != null)
            {
                return new XElement(W.p, errorRun);
            }
            else
            {
                return errorRun;
            }
        }

        private static XElement CreateRunErrorMessage(string errorMessage, TemplateError templateError)
        {
            templateError.HasError = true;
            var errorRun = new XElement(W.r,
                new XElement(W.rPr,
                    new XElement(W.color, new XAttribute(W.val, "FF0000")),
                    new XElement(W.highlight, new XAttribute(W.val, "yellow"))),
                    new XElement(W.t, errorMessage));
            return errorRun;
        }

        private static XElement CreateParaErrorMessage(string errorMessage, TemplateError templateError)
        {
            templateError.HasError = true;
            var errorPara = new XElement(W.p,
                new XElement(W.r,
                    new XElement(W.rPr,
                        new XElement(W.color, new XAttribute(W.val, "FF0000")),
                        new XElement(W.highlight, new XAttribute(W.val, "yellow"))),
                        new XElement(W.t, errorMessage)));
            return errorPara;
        }

        private static string EvaluateXPathToString(XElement element, string xPath, bool optional)
        {
            object xPathSelectResult;
            try
            {
                //support some cells in the table may not have an xpath expression.
                if (string.IsNullOrWhiteSpace(xPath))
                {
                    return string.Empty;
                }

                xPathSelectResult = element.XPathEvaluate(xPath);
            }
            catch (XPathException e)
            {
                throw new XPathException("XPathException: " + e.Message, e);
            }

            if ((xPathSelectResult is IEnumerable) && !(xPathSelectResult is string))
            {
                var selectedData = ((IEnumerable)xPathSelectResult).Cast<XObject>();
                if (!selectedData.Any())
                {
                    if (optional)
                    {
                        return string.Empty;
                    }

                    throw new XPathException(string.Format("XPath expression ({0}) returned no results", xPath));
                }
                if (selectedData.Count() > 1)
                {
                    throw new XPathException(string.Format("XPath expression ({0}) returned more than one node", xPath));
                }

                var selectedDatum = selectedData.First();

                if (selectedDatum is XElement element1)
                {
                    return element1.Value;
                }

                if (selectedDatum is XAttribute)
                {
                    return ((XAttribute)selectedDatum).Value;
                }
            }

            return xPathSelectResult.ToString();
        }
    }
}