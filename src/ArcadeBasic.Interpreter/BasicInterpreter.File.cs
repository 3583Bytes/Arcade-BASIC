using System.Globalization;
using System.Text;
using ArcadeBasic.Parser.Ast;
using ArcadeBasic.Runtime;
using Singulink.Numerics;

namespace ArcadeBasic.Interpreter;

/// <summary>
/// File-I/O statement execution. Phase-5 scope: DISPLAY mode SEQUENTIAL/STREAM,
/// access INPUT/OUTPUT/OUTIN. INTERNAL and BYTE modes, RANDOM organization,
/// ERASE/RESET/RECSIZE/RECTYPE/MARGIN/ZONEWIDTH/WRITE#/READ# are deferred.
/// </summary>
public sealed partial class BasicInterpreter
{
    private FlowControl ExecOpen(OpenStmt stmt, ActivationRecord frame)
    {
        var channel = (int)EvalNumeric(stmt.Channel, frame);
        var path = EvalString(stmt.Name, frame);

        // Map ACCESS + CREATE → System.IO FileMode + FileAccess.
        var access = stmt.Access switch
        {
            OpenAccess.Input => FileAccess.Read,
            OpenAccess.Output => FileAccess.Write,
            OpenAccess.Outin => FileAccess.ReadWrite,
            _ => FileAccess.ReadWrite,
        };

        var mode = stmt.Create switch
        {
            OpenCreate.New => FileMode.CreateNew,
            OpenCreate.Old => FileMode.Open,
            OpenCreate.NewOld => FileMode.OpenOrCreate,
            _ => stmt.Access == OpenAccess.Input ? FileMode.Open : FileMode.OpenOrCreate,
        };

        // ACCESS OUTPUT defaults to truncating on open per spec; we only do that
        // when CREATE wasn't explicitly NEW (which CreateNew already enforces).
        if (stmt.Access == OpenAccess.Output && stmt.Create == OpenCreate.Default)
        {
            mode = FileMode.Create;
        }

        try
        {
            var file = new DisplayFile(path, mode, access)
            {
                IsInternal = stmt.RecType == OpenRecType.Internal,
            };
            _channels.Open(channel, file);
        }
        catch (FileNotFoundException ex)
        {
            throw new BasicRuntimeException(7010, $"file '{path}' not found: {ex.Message}");
        }
        catch (IOException ex)
        {
            throw new BasicRuntimeException(7011, $"OPEN failed for '{path}': {ex.Message}");
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new BasicRuntimeException(7012, $"OPEN denied for '{path}': {ex.Message}");
        }
        return FlowControl.Continue;
    }

    private FlowControl ExecClose(CloseStmt stmt, ActivationRecord frame)
    {
        var channel = (int)EvalNumeric(stmt.Channel, frame);
        _channels.Close(channel);
        return FlowControl.Continue;
    }

    private FlowControl ExecPrintFile(PrintFileStmt stmt, ActivationRecord frame)
    {
        var channel = (int)EvalNumeric(stmt.Channel, frame);
        var file = _channels.Get(channel);

        var sb = new StringBuilder();
        var col = 0;
        var suppressNewline = false;

        for (var i = 0; i < stmt.Items.Count; i++)
        {
            var item = stmt.Items[i];
            switch (item)
            {
                case PrintExprItem ei:
                    var text = FormatForPrint(EvalExpr(ei.Value, frame));
                    sb.Append(text);
                    col += text.Length;
                    suppressNewline = false;
                    break;
                case PrintComma:
                    var next = ((col / DefaultZoneWidth) + 1) * DefaultZoneWidth;
                    sb.Append(' ', next - col);
                    col = next;
                    suppressNewline = i == stmt.Items.Count - 1;
                    break;
                case PrintSemicolon:
                    suppressNewline = i == stmt.Items.Count - 1;
                    break;
            }
        }

        if (suppressNewline) file.Write(sb.ToString());
        else file.WriteLine(sb.ToString());
        return FlowControl.Continue;
    }

    private FlowControl ExecInputFile(InputFileStmt stmt, ActivationRecord frame)
    {
        var channel = (int)EvalNumeric(stmt.Channel, frame);
        var file = _channels.Get(channel);

        var line = file.ReadLine() ?? throw new BasicRuntimeException(7020,
            $"INPUT #{channel}: end of file");
        var fields = line.Split(',');
        if (fields.Length < stmt.Targets.Count)
        {
            throw new BasicRuntimeException(7021,
                $"INPUT #{channel}: line had {fields.Length} field(s), expected {stmt.Targets.Count}");
        }

        for (var i = 0; i < stmt.Targets.Count; i++)
        {
            var target = stmt.Targets[i];
            var raw = fields[i].Trim();
            var isString = TargetIsString(target);
            Value v;
            if (isString)
            {
                // Strip surrounding quotes if present (a courtesy for files
                // written by PRINT # whose string items emerged unquoted).
                if (raw.Length >= 2 && raw[0] == '"' && raw[^1] == '"')
                    raw = raw[1..^1].Replace("\"\"", "\"", StringComparison.Ordinal);
                v = new StringValue(raw);
            }
            else
            {
                if (!BigDecimal.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out var bd))
                    throw new BasicRuntimeException(7022,
                        $"INPUT #{channel}: '{raw}' is not numeric");
                v = new NumericValue(bd);
            }
            WriteAssignableTarget(target, v, frame);
        }
        return FlowControl.Continue;
    }

    private FlowControl ExecLineInputFile(LineInputFileStmt stmt, ActivationRecord frame)
    {
        var channel = (int)EvalNumeric(stmt.Channel, frame);
        var file = _channels.Get(channel);
        var line = file.ReadLine() ?? throw new BasicRuntimeException(7020,
            $"LINE INPUT #{channel}: end of file");
        WriteAssignableTarget(stmt.Target, new StringValue(line), frame);
        return FlowControl.Continue;
    }

    // -- INTERNAL (exact-value) records: WRITE # / READ # ----------------
    // One field per line. Numbers are written at full precision (no display
    // rounding) and read back exactly; strings are the raw line.

    private FlowControl ExecWriteFile(WriteFileStmt stmt, ActivationRecord frame)
    {
        var channel = (int)EvalNumeric(stmt.Channel, frame);
        var file = _channels.Get(channel);
        if (!file.IsInternal)
            throw new BasicRuntimeException(7030, $"WRITE #{channel}: channel is not open RECTYPE INTERNAL");
        foreach (var item in stmt.Items)
        {
            file.WriteLine(FormatInternal(EvalExpr(item, frame)));
        }
        return FlowControl.Continue;
    }

    private FlowControl ExecReadFile(ReadFileStmt stmt, ActivationRecord frame)
    {
        var channel = (int)EvalNumeric(stmt.Channel, frame);
        var file = _channels.Get(channel);
        if (!file.IsInternal)
            throw new BasicRuntimeException(7030, $"READ #{channel}: channel is not open RECTYPE INTERNAL");
        foreach (var target in stmt.Targets)
        {
            var line = file.ReadLine() ?? throw new BasicRuntimeException(7020,
                $"READ #{channel}: end of file");
            Value v;
            if (TargetIsString(target))
            {
                v = new StringValue(line);
            }
            else if (BigDecimal.TryParse(line, NumberStyles.Float, CultureInfo.InvariantCulture, out var bd))
            {
                v = new NumericValue(bd);
            }
            else
            {
                throw new BasicRuntimeException(7022, $"READ #{channel}: '{line}' is not numeric");
            }
            WriteAssignableTarget(target, v, frame);
        }
        return FlowControl.Continue;
    }

    /// <summary>Exact textual encoding of a value for an INTERNAL record: numbers
    /// at full precision (round-trips exactly), strings verbatim.</summary>
    private static string FormatInternal(Value v) => v switch
    {
        NumericValue n => n.V.ToString(CultureInfo.InvariantCulture),
        StringValue s => s.V,
        _ => throw new BasicRuntimeException(7031, "WRITE #: unsupported value type"),
    };
}
