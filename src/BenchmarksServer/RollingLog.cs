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
        private List<string> _lines { get; set; }

        public RollingLog(int capacity)
        {
            _lines = new List<string>(capacity);
            _capacity = capacity;
        }

        public void AddLine(string text)
        {
            if (_lines.Count == _capacity)
            {
                _lines.RemoveAt(0);
            }

            _lines.Add(text);
        }

        public void Clear()
        {
            _lines.Clear();
        }

        public override string ToString()
        {
            _builder.Clear();

            foreach (var line in _lines)
            {
                _builder.AppendLine(line);
            }

            return _builder.ToString();
        }
    }
}
