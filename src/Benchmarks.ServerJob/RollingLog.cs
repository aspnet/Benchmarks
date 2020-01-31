using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Benchmarks.ServerJob
{
    /// <summary>
    /// Prevents logs from getting to big to be rendered on the driver
    /// </summary>
    public class RollingLog
    {
        private readonly int _capacity;
        private StringBuilder _builder = new StringBuilder();
        private List<string> _lines { get; set; }
        private int Discarded { get; set; } // Number of discarded lines

        public RollingLog(int capacity)
        {
            _lines = new List<string>(capacity);
            _capacity = capacity;
        }

        public void AddLine(string text)
        {
            lock (this)
            {
                if (_lines.Count == _capacity)
                {
                    _lines.RemoveAt(0);
                    Discarded++;
                }

                _lines.Add(text);
            }
        }

        public string LastLine
        {
            get
            {
                lock (this)
                {
                    return _lines == null || _lines.Count == 0 ? "" : _lines[_lines.Count - 1];
                }
            }
        }

        public string[] Get(int skip)
        {
            lock (this)
            {
                return _lines.Skip(Math.Min(0, skip - Discarded)).ToArray();
            }
        }

        public string[] Get(int skip, int take)
        {
            lock (this)
            {
                return _lines.Skip(Math.Min(0, skip - Discarded)).Take(take).ToArray();
            }
        }

        public void Clear()
        {
            lock (this)
            {
                _lines.Clear();
                Discarded = 0;
            }
        }

        public override string ToString()
        {
            lock (this)
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
}
