using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.Versioning;
using Xunit;
using CScore.Import;

namespace CScore.Tests.Import
{
    public class LiraImportApiTests
    {
        // This test requires LIRA-SAPR to be installed and the specified file to be currently OPEN in it.
        // It reads topology from the live COM API and compares it byte-by-byte with our binary parser.
        [Fact(Skip = "Requires LIRA-SAPR installed and 'hostel.lir' to be open in it. Run manually for verification.")]
        [SupportedOSPlatform("windows")]
        public void CompareComApiAndBinaryParser()
        {
            string path = @"C:\Users\palex\Documents\prj\obshezitie_belgorod\calc\hostel.lir";
            Assert.True(File.Exists(path), "File must exist to compare");

            // 1. Read from API
            var appType = Type.GetTypeFromProgID("LiraSapr.Application");
            Assert.NotNull(appType);

            dynamic lira = Activator.CreateInstance(appType!)!;
            dynamic doc = lira.ActiveDocument;
            Assert.NotNull(doc);

            var apiData = new LiraSchemaData();
            var diag = new List<string>();

            // Read Nodes
            dynamic nodesTable = doc.AllTables.CreateNewItem(2);
            object? rawNodes = null;
            nodesTable.GetContents(ref rawNodes);
            ParseApiNodes(rawNodes as object[,], apiData);

            // Read Elems
            dynamic elemsTable = doc.AllTables.CreateNewItem(3);
            object? rawElems = null;
            elemsTable.GetContents(ref rawElems);
            ParseApiElements(rawElems as object[,], apiData);

            Assert.NotEmpty(apiData.Nodes);
            Assert.NotEmpty(apiData.Elements);

            // 2. Read from Binary
            var fileData = LiraFileParser.Parse(path);
            
            Assert.NotEmpty(fileData.Nodes);
            Assert.NotEmpty(fileData.Elements);

            // 3. Compare Nodes
            var apiNodesDict = apiData.Nodes.ToDictionary(n => n.Id);
            var fileNodesDict = fileData.Nodes.ToDictionary(n => n.Id);
            
            int nodeMismatches = 0;
            foreach (var kvp in apiNodesDict)
            {
                Assert.True(fileNodesDict.TryGetValue(kvp.Key, out var fileNode), $"Node {kvp.Key} missing in binary");
                
                double tol = 1e-4;
                if (Math.Abs(kvp.Value.X - fileNode.X) > tol ||
                    Math.Abs(kvp.Value.Y - fileNode.Y) > tol ||
                    Math.Abs(kvp.Value.Z - fileNode.Z) > tol)
                {
                    nodeMismatches++;
                }
            }
            Assert.Equal(0, nodeMismatches);

            // 4. Compare Elements
            var apiElemsDict = apiData.Elements.ToDictionary(e => e.Id);
            var fileElemsDict = fileData.Elements.ToDictionary(e => e.Id);
            
            int elemMissingInFile = 0;
            int elemNodesMismatch = 0;
            
            foreach (var kvp in apiElemsDict)
            {
                if (!fileElemsDict.TryGetValue(kvp.Key, out var fileElem))
                {
                    elemMissingInFile++;
                    continue;
                }
                
                bool nodesMatch = kvp.Value.NodeIds.Length == fileElem.NodeIds.Length;
                if (nodesMatch)
                {
                    for (int i = 0; i < kvp.Value.NodeIds.Length; i++)
                    {
                        if (kvp.Value.NodeIds[i] != fileElem.NodeIds[i])
                        {
                            nodesMatch = false; break;
                        }
                    }
                }
                
                if (!nodesMatch)
                {
                    elemNodesMismatch++;
                }
            }
            
            Assert.Equal(0, elemMissingInFile);
            Assert.Equal(0, elemNodesMismatch);
        }

        // --- API Parsing Helpers ---
        static void ParseApiNodes(object[,]? rows, LiraSchemaData data)
        {
            if (rows == null) return;
            int count = rows.GetLength(0);
            for (int i = 0; i < count; i++)
            {
                if (!int.TryParse(rows[i, 0]?.ToString(), out int id)) continue;
                double x = ToDouble(rows[i, 1]);
                double y = ToDouble(rows[i, 2]);
                double z = ToDouble(rows[i, 3]);
                data.Nodes.Add(new LiraNodeRecord(id, x, y, z, 0));
            }
        }

        static void ParseApiElements(object[,]? rows, LiraSchemaData data)
        {
            if (rows == null) return;
            int count = rows.GetLength(0);
            int cols  = rows.GetLength(1);
            for (int i = 0; i < count; i++)
            {
                if (!int.TryParse(rows[i, 0]?.ToString(), out int id)) continue;
                if (!int.TryParse(rows[i, 1]?.ToString(), out int feType)) continue;

                string nodeIdsStr = cols == 3 ? (rows[i, 2]?.ToString() ?? "") : (rows[i, cols - 1]?.ToString() ?? "");
                var nodeIds = ParseNodeIds(nodeIdsStr);
                if (nodeIds.Length > 0)
                    data.Elements.Add(new LiraElementRecord(id, feType, 0, 0, nodeIds));
            }
        }

        static int[] ParseNodeIds(string s)
        {
            var parts = s.Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            var result = new List<int>();
            foreach (var p in parts)
                if (int.TryParse(p.Trim(), out int n) && n > 0)
                    result.Add(n);
            return result.ToArray();
        }

        static double ToDouble(object cell)
        {
            if (cell == null) return 0;
            return cell is double d ? d : double.TryParse(cell.ToString(), out double v) ? v : 0;
        }
    }
}
