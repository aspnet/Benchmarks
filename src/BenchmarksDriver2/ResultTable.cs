using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace BenchmarksDriver
{
    // Renders a markdown table
    public class ResultTable
    {
        public ResultTable(int columns)
        {
            Columns = columns;
        }

        public int Columns { get; }
        public List<string> Headers { get; } = new List<string>();
        public List<List<Cell>> Rows { get; } = new List<List<Cell>>();

        public List<Cell> AddRow()
        {
            var row = new List<Cell>();

            Rows.Add(row);

            return row;
        }

        /// <summary>
        /// Returns the most suited column widths for this table
        /// </summary>
        /// <returns></returns>
        public int[] CalculateColumnWidths()
        {
            var results = new List<int>();

            for (var i = 0; i < Columns; i++)
            {
                var width = 0;

                foreach(var row in Rows)
                {
                    // The width is the sum of each element's width, plus 1 for each separator between elements
                    width = Math.Max(width, row[i].Elements.Select(x => x.Text.Length).Sum() + row[i].Elements.Count() - 1);
                }

                width = Math.Max(width, Math.Max(Headers[i].Length, 1));

                results.Add(width);
            }


            return results.ToArray();
        }

        public void Render(TextWriter writer)
        {
            Render(writer, CalculateColumnWidths());
        }

        public void Render(TextWriter writer, int[] columnWidths)
        {
            /*
              | Tables        | Are           | Cool  |
              | ------------- | ------------- | ----- |
              | col 3 is      | right-aligned | $1600 |
              | col 2 is      | centered      |   $12 |
              | zebra stripes | are neat      |    $1 |
            */

            // | Tables        | Are           | Cool  |
            for (var i = 0; i < Columns; i++)
            {
                writer.Write($"| {Headers[i].PadRight(columnWidths[i])} ");
            }

            writer.WriteLine("|");

            // | ------------- | ------------- | ----- |
            for (var i = 0; i < Columns; i++)
            {
                writer.Write($"| {new String('-', columnWidths[i])} ");
            }

            writer.WriteLine("|");

            // | col 3 is      | right-aligned | $1600 |

            foreach (var row in Rows)
            {
                for (var i = 0; i < Columns; i++)
                {
                    writer.Write($"| ");

                    if (row[i].Elements.Any())
                    {
                        foreach (var element in row[i].Elements.Where(x => x.Alignment == CellTextAlignment.Left))
                        {
                            writer.Write(element.Text);
                            writer.Write(" ");
                        }

                        foreach (var element in row[i].Elements.Where(x => x.Alignment == CellTextAlignment.Unspecified))
                        {
                            writer.Write(element.Text);
                            writer.Write(" ");
                        }

                        // Add spaces to fill between Left and Right aligned elements
                        var leftElements = row[i].Elements.Where(x => x.Alignment != CellTextAlignment.Right);
                        var rightElements = row[i].Elements.Where(x => x.Alignment == CellTextAlignment.Right);

                        var leftWitdh = leftElements.Any() ? leftElements.Sum(x => x.Text.Length + 1) - 1 : 0;
                        var rightWidth = rightElements.Any() ? rightElements.Sum(x => x.Text.Length + 1) - 1 : 0;

                        writer.Write(new String(' ', columnWidths[i] - rightWidth - leftWitdh));

                        foreach (var element in row[i].Elements.Where(x => x.Alignment == CellTextAlignment.Right))
                        {
                            writer.Write(element.Text);
                            writer.Write(" ");
                        }
                    }
                    else
                    {
                        writer.Write(new String(' ', columnWidths[i] + 1));
                    }
                }

                writer.WriteLine($"|");
            }
        }
    }

    public class Cell
    {
        public List<CellElement> Elements { get; } = new List<CellElement>();
    }

    public class CellElement
    {
        public string Text { get; set; }
        public CellTextAlignment Alignment { get; set; }
    }

    public enum CellTextAlignment
    {
        Unspecified,
        Left,
        Right
    }
}
