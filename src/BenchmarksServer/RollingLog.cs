using System;
using System.Collections.Generic;
using System.Text;

namespace BenchmarksServer
{
    /// <summary>
    /// Prevents logs from getting to big to be rendered on the driver
    /// </summary>
    public class RollingLog
    {
        private readonly int _capacity;
        private StringBuilder _builder = new StringBuilder();

        public List<string> Lines { get; set; }

        public RollingLog(int capacity)
        {
            Lines = new List<string>(capacity);
            _capacity = capacity;
        }

        public void AddLine(string text)
        {
            if (Lines.Count == _capacity)
            {
                Lines.RemoveAt(0);
            }

            Lines.Add(text);
        }

        public void Clear()
        {
            Lines.Clear();
        }

        public override string ToString()
        {
            _builder.Clear();

            foreach (var line in Lines)
            {
                _builder.AppendLine(line);
            }

            return _builder.ToString();
        }
    }
}
