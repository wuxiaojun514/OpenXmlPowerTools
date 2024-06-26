﻿using Codeuctivity.OpenXmlPowerTools;
using DocumentFormat.OpenXml.Packaging;
using System;
using System.IO;

namespace TextReplacer02
{
    internal class Program
    {
        private static void Main()
        {
            var n = DateTime.Now;
            var tempDi = new DirectoryInfo(string.Format("ExampleOutput-{0:00}-{1:00}-{2:00}-{3:00}{4:00}{5:00}", n.Year - 2000, n.Month, n.Day, n.Hour, n.Minute, n.Second));
            tempDi.Create();

            var di2 = new DirectoryInfo("../../");
            foreach (var file in di2.GetFiles("*.docx"))
            {
                file.CopyTo(Path.Combine(tempDi.FullName, file.Name));
            }

            using (var doc = WordprocessingDocument.Open(Path.Combine(tempDi.FullName, "Test01.docx"), true))
            {
                TextReplacer.SearchAndReplace(doc, "the", "this", false);
            }

            try
            {
                using var doc = WordprocessingDocument.Open(Path.Combine(tempDi.FullName, "Test02.docx"), true);
                TextReplacer.SearchAndReplace(doc, "the", "this", false);
            }
            catch (Exception) { }
            try
            {
                using var doc = WordprocessingDocument.Open(Path.Combine(tempDi.FullName, "Test03.docx"), true);
                TextReplacer.SearchAndReplace(doc, "the", "this", false);
            }
            catch (Exception) { }
            using (var doc = WordprocessingDocument.Open(Path.Combine(tempDi.FullName, "Test04.docx"), true))
            {
                TextReplacer.SearchAndReplace(doc, "the", "this", true);
            }

            using (var doc = WordprocessingDocument.Open(Path.Combine(tempDi.FullName, "Test05.docx"), true))
            {
                TextReplacer.SearchAndReplace(doc, "is on", "is above", true);
            }

            using (var doc = WordprocessingDocument.Open(Path.Combine(tempDi.FullName, "Test06.docx"), true))
            {
                TextReplacer.SearchAndReplace(doc, "the", "this", false);
            }

            using (var doc = WordprocessingDocument.Open(Path.Combine(tempDi.FullName, "Test07.docx"), true))
            {
                TextReplacer.SearchAndReplace(doc, "the", "this", true);
            }

            using (var doc = WordprocessingDocument.Open(Path.Combine(tempDi.FullName, "Test08.docx"), true))
            {
                TextReplacer.SearchAndReplace(doc, "the", "this", true);
            }

            using (var doc = WordprocessingDocument.Open(Path.Combine(tempDi.FullName, "Test09.docx"), true))
            {
                TextReplacer.SearchAndReplace(doc, "===== Replace this text =====", "***zzz***", true);
            }
        }
    }
}