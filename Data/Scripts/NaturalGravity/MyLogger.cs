using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Sandbox.ModAPI;
using Sandbox.Common;
namespace Scripts.KSWH
{
    class MyLogger
    {
        private System.IO.TextWriter m_writer;
        private int m_indent = 0;
        private StringBuilder m_cache = new StringBuilder();
        
        public MyLogger(string logFile)
        {
            m_writer = MyAPIGateway.Utilities.WriteFileInLocalStorage(logFile, typeof(MyLogger));
        }
        public void IncreaseIndent()
        {
            m_indent++;
        }
        public void DecreaseIndent()
        {
            if(m_indent > 0)
                m_indent--;
        }
        public void WriteLine(string text)
        {
            if (m_cache.Length > 0)
                m_writer.WriteLine(m_cache);
            m_cache.Clear();
            m_cache.Append(DateTime.Now.ToString("[HH:mm:ss] "));
            for (int i = 0; i < m_indent; i++)
                m_cache.Append("\t");
            m_writer.WriteLine(m_cache.Append(text));
            m_writer.Flush();
            m_cache.Clear();
        }
        public void Write(string text)
        {
            m_cache.Append(text);
        }
        internal void Close()
        {
            if (m_cache.Length > 0)
                m_writer.WriteLine(m_cache);
            m_writer.Flush();
            m_writer.Close();
        }
    }
}
