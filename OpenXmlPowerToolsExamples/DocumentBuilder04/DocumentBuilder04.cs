﻿using Codeuctivity.OpenXmlPowerTools;
using Codeuctivity.OpenXmlPowerTools.DocumentBuilder;
using System;
using System.IO;
using System.Linq;

namespace DocumentBuilder04
{
    internal class ContentControlsExample
    {
        private static void Main()
        {
            var n = DateTime.Now;
            var tempDi = new DirectoryInfo(string.Format("ExampleOutput-{0:00}-{1:00}-{2:00}-{3:00}{4:00}{5:00}", n.Year - 2000, n.Month, n.Day, n.Hour, n.Minute, n.Second));
            tempDi.Create();

            var solarSystemDoc = new WmlDocument("../../solar-system.docx");
            using var streamDoc = new OpenXmlMemoryStreamDocument(solarSystemDoc);
            using var solarSystem = streamDoc.GetWordprocessingDocument();
            // get children elements of the <w:body> element
            var q1 = solarSystem
                .MainDocumentPart
                .GetXDocument()
                .Root
                .Element(W.body)
                .Elements();

            // project collection of tuples containing element and type
            var q2 = q1
                .Select(
                    e =>
                    {
                        var keyForGroupAdjacent = ".NonContentControl";
                        if (e.Name == W.sdt)
                        {
                            keyForGroupAdjacent = e.Element(W.sdtPr)
                                .Element(W.tag)
                                .Attribute(W.val)
                                .Value;
                        }

                        if (e.Name == W.sectPr)
                        {
                            keyForGroupAdjacent = null;
                        }

                        return new
                        {
                            Element = e,
                            KeyForGroupAdjacent = keyForGroupAdjacent
                        };
                    }
                ).Where(e => e.KeyForGroupAdjacent != null);

            // group by type
            var q3 = q2.GroupAdjacent(e => e.KeyForGroupAdjacent);

            // temporary code to dump q3
            foreach (var g in q3)
            {
                Console.WriteLine("{0}:  {1}", g.Key, g.Count());
            }
            //Environment.Exit(0);

            // validate existence of files referenced in content controls
            foreach (var f in q3.Where(g => g.Key != ".NonContentControl"))
            {
                var filename = "../../" + f.Key + ".docx";
                var fi = new FileInfo(filename);
                if (!fi.Exists)
                {
                    Console.WriteLine("{0} doesn't exist.", filename);
                    Environment.Exit(0);
                }
            }

            // project collection with opened WordProcessingDocument
            var q4 = q3
                .Select(g => new
                {
                    Group = g,
                    Document = g.Key != ".NonContentControl" ?
                        new WmlDocument("../../" + g.Key + ".docx") :
                        solarSystemDoc
                });

            // project collection of OpenXml.PowerTools.Source
            var sources = q4
                .Select(
                    g =>
                    {
                        if (g.Group.Key == ".NonContentControl")
                        {
                            return new Source(
                                g.Document,
                                g.Group
                                    .First()
                                    .Element
                                    .ElementsBeforeSelf()
                                    .Count(),
                                g.Group
                                    .Count(),
                                false);
                        }
                        else
                        {
                            return new Source(g.Document, false);
                        }
                    }
                ).ToList();

            DocumentBuilder.BuildDocument(sources, Path.Combine(tempDi.FullName, "solar-system-new.docx"));
        }
    }
}