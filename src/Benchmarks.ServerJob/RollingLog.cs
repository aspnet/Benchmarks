using System.Collections;
using System.Collections.Generic;
using System.Text;

namespace Benchmarks.ServerJob
{
    /// <summary>
    /// Prevents logs from getting to big to be rendered on the driver
    /// </summary>
    public class RollingLog : IEnumerable<string>
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
            if (_lines.Count == _capacity)
            {
                _lines.RemoveAt(0);
                Discarded++;
            }

            _lines.Add(text);
        }

        public string LastLine => _lines == null || _lines.Count == 0 ? "" : _lines[_lines.Count - 1];

        public void Clear()
        {
            _lines.Clear();
            Discarded = 0;
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

        public IEnumerator<string> GetEnumerator()
        {
            return _lines.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return _lines.GetEnumerator();
        }
    }
}
