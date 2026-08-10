using System.Text;

namespace NesLifter.CodeGen;

public sealed class CodeWriter
{
    private readonly StringBuilder _sb = new();
    private int _indent;
    private bool _isNewLine = true;

    public CodeWriter AppendLine(string text = "")
    {
        if (string.IsNullOrEmpty(text))
        {
            _sb.AppendLine();
            _isNewLine = true;
            return this;
        }

        if (_isNewLine && _indent > 0)
            _sb.Append('\t', _indent);
            
        _sb.AppendLine(text);
        _isNewLine = true;
        return this;
    }

    public Scope Block()
    {
        AppendLine("{");
        _indent++;
        return new Scope(this);
    }

    public Scope Method(string signature)
    {
        AppendLine(signature);
        return Block();
    }
    
    public Scope If(string condition)
    {
        AppendLine($"if ({condition})");
        return Block();
    }

    public override string ToString() => _sb.ToString();

    public readonly struct Scope : IDisposable
    {
        private readonly CodeWriter _writer;
        private readonly bool _isOwner;

        public Scope(CodeWriter writer)
        {
            _writer = writer;
            _isOwner = true;
        }

        public void Dispose()
        {
            if (!_isOwner) return;
            _writer._indent--;
            _writer.AppendLine("}");
        }
    }
}