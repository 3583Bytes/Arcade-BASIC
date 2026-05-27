using System.Globalization;
using System.Text;
using ArcadeBasic.Bytecode;
using ArcadeBasic.Runtime;
using Singulink.Numerics;
using BcProgram = ArcadeBasic.Bytecode.Program;

namespace ArcadeBasic.Vm;

/// <summary>
/// Phase-9 stack-based bytecode VM. Same Value record types and activation-
/// record model as the tree-walker, so shared helpers (BuiltinImpls,
/// FormatNumeric) compose without translation.
/// </summary>
public sealed class BasicVm
{
    private readonly BcProgram _program;
    private readonly TextWriter _out;
    private readonly TextReader _in;

    private const int DefaultZoneWidth = 16;

    public BasicVm(BcProgram program, TextWriter @out, TextReader @in)
    {
        _program = program;
        _out = @out;
        _in = @in;
    }

    public int Run()
    {
        try
        {
            var programFrame = new ActivationRecord(_program.Main.FrameSize, parent: null);
            ExecuteChunk(_program.Main, programFrame, programFrame);
            return 0;
        }
        catch (BasicRuntimeException ex)
        {
            _out.Flush();
            Console.Error.WriteLine($"runtime error [{ex.TypeCode}]: {ex.Message}");
            return 1;
        }
    }

    /// <summary>Returns true if the chunk exited via End/Stop (program halts).</summary>
    private bool ExecuteChunk(Chunk chunk, ActivationRecord frame, ActivationRecord programFrame)
    {
        var code = chunk.Code;
        var stack = new Stack<Value>(64);
        var pc = 0;
        var col = 0; // current PRINT column for zone padding
        var pendingNewline = false;

        while (pc < code.Count)
        {
            var op = (Opcode)code[pc++];
            switch (op)
            {
                case Opcode.Halt: return true;
                case Opcode.Stop: return true;
                case Opcode.End: return true;
                case Opcode.Nop: break;

                case Opcode.Pop: stack.Pop(); break;
                case Opcode.Dup: stack.Push(stack.Peek()); break;
                case Opcode.Swap:
                    var s1 = stack.Pop();
                    var s2 = stack.Pop();
                    stack.Push(s1); stack.Push(s2);
                    break;

                case Opcode.LoadConstNumber:
                    stack.Push(new NumericValue(chunk.Numbers[(int)ReadU32(code, ref pc)]));
                    break;
                case Opcode.LoadConstString:
                    stack.Push(new StringValue(chunk.Strings[(int)ReadU32(code, ref pc)]));
                    break;
                case Opcode.LoadZero: stack.Push(NumericValue.Zero); break;
                case Opcode.LoadOne: stack.Push(NumericValue.One); break;
                case Opcode.LoadMinusOne: stack.Push(NumericValue.MinusOne); break;

                case Opcode.LoadLocal:
                    {
                        var slot = (int)ReadU32(code, ref pc);
                        stack.Push(frame.GetOrDefault(slot, NumericValue.Zero));
                        break;
                    }
                case Opcode.StoreLocal:
                    {
                        var slot = (int)ReadU32(code, ref pc);
                        frame.Set(slot, stack.Pop());
                        break;
                    }
                case Opcode.LoadOuter:
                    {
                        var depth = (int)ReadU32(code, ref pc);
                        var slot = (int)ReadU32(code, ref pc);
                        var f = frame;
                        for (var i = 0; i < depth && f is not null; i++) f = f.Parent;
                        f ??= programFrame;
                        stack.Push(f.GetOrDefault(slot, NumericValue.Zero));
                        break;
                    }
                case Opcode.StoreOuter:
                    {
                        var depth = (int)ReadU32(code, ref pc);
                        var slot = (int)ReadU32(code, ref pc);
                        var f = frame;
                        for (var i = 0; i < depth && f is not null; i++) f = f.Parent;
                        f ??= programFrame;
                        f.Set(slot, stack.Pop());
                        break;
                    }

                case Opcode.Add: BinaryNumeric(stack, (a, b) => a + b); break;
                case Opcode.Sub: BinaryNumeric(stack, (a, b) => a - b); break;
                case Opcode.Mul: BinaryNumeric(stack, (a, b) => a * b); break;
                case Opcode.Div: BinaryNumeric(stack, (a, b) =>
                {
                    if (b == BigDecimal.Zero) throw new BasicRuntimeException(1001, "division by zero");
                    return BigDecimal.Divide(a, b, 30, RoundingMode.MidpointToEven);
                }); break;
                case Opcode.Pow: BinaryNumeric(stack, Pow); break;
                case Opcode.Mod: BinaryNumeric(stack, (a, b) =>
                {
                    if (b == BigDecimal.Zero) throw new BasicRuntimeException(1001, "MOD by zero");
                    return a - BigDecimal.Floor(a / b) * b;
                }); break;
                case Opcode.Rem: BinaryNumeric(stack, (a, b) =>
                {
                    if (b == BigDecimal.Zero) throw new BasicRuntimeException(1001, "REMAINDER by zero");
                    return a - BigDecimal.Truncate(a / b) * b;
                }); break;
                case Opcode.Concat:
                    {
                        var br = ((StringValue)stack.Pop()).V;
                        var bl = ((StringValue)stack.Pop()).V;
                        stack.Push(new StringValue(bl + br));
                        break;
                    }
                case Opcode.Neg:
                    stack.Push(new NumericValue(-((NumericValue)stack.Pop()).V));
                    break;

                case Opcode.Eq: Compare(stack, (a, b) => a == b, (a, b) => a == b); break;
                case Opcode.Ne: Compare(stack, (a, b) => a != b, (a, b) => a != b); break;
                case Opcode.Lt: Compare(stack, (a, b) => a < b, (a, b) => string.CompareOrdinal(a, b) < 0); break;
                case Opcode.Le: Compare(stack, (a, b) => a <= b, (a, b) => string.CompareOrdinal(a, b) <= 0); break;
                case Opcode.Gt: Compare(stack, (a, b) => a > b, (a, b) => string.CompareOrdinal(a, b) > 0); break;
                case Opcode.Ge: Compare(stack, (a, b) => a >= b, (a, b) => string.CompareOrdinal(a, b) >= 0); break;

                case Opcode.And: BinaryNumeric(stack, (a, b) =>
                    a != BigDecimal.Zero && b != BigDecimal.Zero ? -BigDecimal.One : BigDecimal.Zero); break;
                case Opcode.Or: BinaryNumeric(stack, (a, b) =>
                    a != BigDecimal.Zero || b != BigDecimal.Zero ? -BigDecimal.One : BigDecimal.Zero); break;
                case Opcode.Xor: BinaryNumeric(stack, (a, b) =>
                    (a != BigDecimal.Zero) != (b != BigDecimal.Zero) ? -BigDecimal.One : BigDecimal.Zero); break;
                case Opcode.Not:
                    {
                        var v = ((NumericValue)stack.Pop()).V;
                        stack.Push(v == BigDecimal.Zero ? NumericValue.One : NumericValue.Zero);
                        break;
                    }
                case Opcode.Imp: BinaryNumeric(stack, (a, b) =>
                    a == BigDecimal.Zero || b != BigDecimal.Zero ? -BigDecimal.One : BigDecimal.Zero); break;
                case Opcode.Eqv: BinaryNumeric(stack, (a, b) =>
                    (a != BigDecimal.Zero) == (b != BigDecimal.Zero) ? -BigDecimal.One : BigDecimal.Zero); break;
                case Opcode.Band: BinaryNumeric(stack, (a, b) =>
                    BigDecimal.Parse(((long)a & (long)b).ToString())); break;
                case Opcode.Bor: BinaryNumeric(stack, (a, b) =>
                    BigDecimal.Parse(((long)a | (long)b).ToString())); break;
                case Opcode.Bxor: BinaryNumeric(stack, (a, b) =>
                    BigDecimal.Parse(((long)a ^ (long)b).ToString())); break;
                case Opcode.Bnot:
                    {
                        var v = (long)((NumericValue)stack.Pop()).V;
                        stack.Push(new NumericValue(BigDecimal.Parse((~v).ToString())));
                        break;
                    }

                case Opcode.Jump:
                    {
                        var off = ReadI32(code, ref pc);
                        pc += off;
                        break;
                    }
                case Opcode.JumpIfTrue:
                    {
                        var off = ReadI32(code, ref pc);
                        if (((NumericValue)stack.Pop()).V != BigDecimal.Zero) pc += off;
                        break;
                    }
                case Opcode.JumpIfFalse:
                    {
                        var off = ReadI32(code, ref pc);
                        if (((NumericValue)stack.Pop()).V == BigDecimal.Zero) pc += off;
                        break;
                    }

                case Opcode.CallBuiltin:
                    {
                        var bid = (int)ReadU32(code, ref pc);
                        var argc = (int)ReadU32(code, ref pc);
                        var name = _program.BuiltinNames[bid];
                        var args = new Value[argc];
                        for (var i = argc - 1; i >= 0; i--) args[i] = stack.Pop();
                        if (BuiltinImpls.All.TryGetValue(name, out var fn))
                        {
                            stack.Push(fn(args));
                        }
                        else if (string.Equals(name, "EXTYPE", StringComparison.OrdinalIgnoreCase)
                              || string.Equals(name, "EXLINE", StringComparison.OrdinalIgnoreCase))
                        {
                            stack.Push(NumericValue.Zero);
                        }
                        else if (string.Equals(name, "EXTEXT", StringComparison.OrdinalIgnoreCase))
                        {
                            stack.Push(StringValue.Empty);
                        }
                        else
                        {
                            throw new BasicRuntimeException(0, $"builtin '{name}' not implemented");
                        }
                        break;
                    }
                case Opcode.CallSub:
                    {
                        var sid = (int)ReadU32(code, ref pc);
                        var argc = (int)ReadU32(code, ref pc);
                        var sub = _program.Subs[sid];
                        var args = new Value[argc];
                        for (var i = argc - 1; i >= 0; i--) args[i] = stack.Pop();
                        var subFrame = new ActivationRecord(sub.Body.FrameSize, programFrame);
                        for (var i = 0; i < argc; i++) subFrame.Set(i, args[i]);
                        ExecuteChunk(sub.Body, subFrame, programFrame);
                        break;
                    }
                case Opcode.CallFunction:
                    {
                        var fid = (int)ReadU32(code, ref pc);
                        var argc = (int)ReadU32(code, ref pc);
                        var fn = _program.Functions[fid];
                        var args = new Value[argc];
                        for (var i = argc - 1; i >= 0; i--) args[i] = stack.Pop();
                        var fnFrame = new ActivationRecord(fn.Body.FrameSize, programFrame);
                        for (var i = 0; i < argc; i++) fnFrame.Set(i, args[i]);
                        // Run; the function chunk's body ends with LoadLocal(returnSlot) + LeaveFunction
                        // which means whatever the body left in the return slot bubbles up.
                        // Capture by re-running the chunk and reading the slot.
                        ExecuteChunk(fn.Body, fnFrame, programFrame);
                        stack.Push(fnFrame.GetOrDefault(fn.ReturnSlot,
                            fn.IsString ? StringValue.Empty : NumericValue.Zero));
                        break;
                    }
                case Opcode.CallDef:
                    throw new BasicRuntimeException(0, "DEF calls are not yet supported by the VM (Phase-9 limitation)");
                case Opcode.LeaveSub: return false;
                case Opcode.LeaveFunction: return false;

                case Opcode.PrintNumber:
                    {
                        var v = ((NumericValue)stack.Pop()).V;
                        var text = FormatNumeric(v);
                        _out.Write(text);
                        col += text.Length;
                        pendingNewline = true;
                        break;
                    }
                case Opcode.PrintString:
                    {
                        var s = ((StringValue)stack.Pop()).V;
                        _out.Write(s);
                        col += s.Length;
                        pendingNewline = true;
                        break;
                    }
                case Opcode.PrintNewline:
                    _out.WriteLine();
                    col = 0;
                    pendingNewline = false;
                    break;
                case Opcode.PrintZonePad:
                    {
                        var next = ((col / DefaultZoneWidth) + 1) * DefaultZoneWidth;
                        for (var i = col; i < next; i++) _out.Write(' ');
                        col = next;
                        break;
                    }

                case Opcode.LoadConstantPi:
                    stack.Push(BuiltinImpls.EvalConstant("PI"));
                    break;
                case Opcode.LoadConstantEps:
                    stack.Push(BuiltinImpls.EvalConstant("EPS"));
                    break;
                case Opcode.LoadConstantInf:
                    stack.Push(BuiltinImpls.EvalConstant("INF"));
                    break;
                case Opcode.LoadConstantMaxnum:
                    stack.Push(BuiltinImpls.EvalConstant("MAXNUM"));
                    break;

                default:
                    throw new BasicRuntimeException(0, $"unimplemented opcode {op}");
            }
        }

        _ = pendingNewline;
        return false;
    }

    private static uint ReadU32(IReadOnlyList<byte> code, ref int pc)
    {
        uint v = code[pc++];
        v |= (uint)code[pc++] << 8;
        v |= (uint)code[pc++] << 16;
        v |= (uint)code[pc++] << 24;
        return v;
    }

    private static int ReadI32(IReadOnlyList<byte> code, ref int pc) => (int)ReadU32(code, ref pc);

    private static void BinaryNumeric(Stack<Value> stack, Func<BigDecimal, BigDecimal, BigDecimal> op)
    {
        var b = ((NumericValue)stack.Pop()).V;
        var a = ((NumericValue)stack.Pop()).V;
        stack.Push(new NumericValue(op(a, b)));
    }

    private static void Compare(Stack<Value> stack,
        Func<BigDecimal, BigDecimal, bool> numericOp,
        Func<string, string, bool> stringOp)
    {
        var b = stack.Pop();
        var a = stack.Pop();
        bool result = (a, b) switch
        {
            (NumericValue x, NumericValue y) => numericOp(x.V, y.V),
            (StringValue x, StringValue y) => stringOp(x.V, y.V),
            _ => throw new BasicRuntimeException(0, "type mismatch in comparison"),
        };
        stack.Push(result ? NumericValue.MinusOne : NumericValue.Zero);
    }

    private static BigDecimal Pow(BigDecimal a, BigDecimal b)
    {
        if (b == BigDecimal.Truncate(b) && b >= int.MinValue && b <= int.MaxValue)
        {
            return BigDecimal.Pow(a, (int)b);
        }
        var ad = double.Parse(a.ToString(), CultureInfo.InvariantCulture);
        var bd = double.Parse(b.ToString(), CultureInfo.InvariantCulture);
        return BigDecimal.Parse(Math.Pow(ad, bd).ToString("R", CultureInfo.InvariantCulture));
    }

    private static string FormatNumeric(BigDecimal x)
    {
        var s = x.ToString();
        return x >= BigDecimal.Zero ? " " + s + " " : s + " ";
    }
}
