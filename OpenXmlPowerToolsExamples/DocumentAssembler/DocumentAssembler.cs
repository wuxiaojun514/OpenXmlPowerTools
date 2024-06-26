﻿using Codeuctivity.OpenXmlPowerTools;
using System;
using System.IO;
using System.Xml.Linq;

namespace DocumentAssembler
{
    internal class Program
    {
        private static void Main(string[] args)
        {
            if (args.Length != 3)
            {
                PrintUsage();
                Environment.Exit(0);
            }

            var templateDoc = new FileInfo(args[0]);
            if (!templateDoc.Exists)
            {
                Console.WriteLine("Error, {0} does not exist.", args[0]);
                PrintUsage();
                Environment.Exit(0);
            }
            var dataFile = new FileInfo(args[1]);
            if (!dataFile.Exists)
            {
                Console.WriteLine("Error, {0} does not exist.", args[1]);
                PrintUsage();
                Environment.Exit(0);
            }
            var assembledDoc = new FileInfo(args[2]);
            if (assembledDoc.Exists)
            {
                Console.WriteLine("Error, {0} exists.", args[2]);
                PrintUsage();
                Environment.Exit(0);
            }

            var wmlDoc = new WmlDocument(templateDoc.FullName);
            var data = XElement.Load(dataFile.FullName);
            var wmlAssembledDoc = Codeuctivity.OpenXmlPowerTools.DocumentAssembler.AssembleDocument(wmlDoc, data, out var templateError);
            if (templateError)
            {
                Console.WriteLine("Errors in template.");
                Console.WriteLine("See {0} to determine the errors in the template.", assembledDoc.Name);
            }

            wmlAssembledDoc.SaveAs(assembledDoc.FullName);
        }

        private static void PrintUsage()
        {
            Console.WriteLine("Usage: DocumentAssembler TemplateDocument.docx Data.xml AssembledDoc.docx");
        }
    }
}