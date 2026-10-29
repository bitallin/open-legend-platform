using System;
using System.Collections.Generic;

namespace Legend.ClientCore
{
    /// <summary>Ring buffer for chat lines shown in the MVP HUD.</summary>
    public sealed class ChatLogBuffer
    {
        private readonly LinkedList<string> _lines = new LinkedList<string>();
        public int Capacity { get; }

        public ChatLogBuffer(int capacity = 40)
        {
            Capacity = Math.Max(1, capacity);
        }

        public IReadOnlyCollection<string> Lines => _lines;

        public void Add(string line)
        {
            if (string.IsNullOrWhiteSpace(line)) return;
            _lines.AddLast(line.Trim());
            while (_lines.Count > Capacity)
                _lines.RemoveFirst();
        }

        public void Clear() => _lines.Clear();
    }
}
