using Singulink.Numerics;

// Phase 0 spike: confirm Singulink.Numerics.BigDecimal works under Native AOT
// and exercises the rounding modes we'll need for spec-conformant arithmetic.

if (args.Length > 0 && args[0] == "--version")
{
    Console.WriteLine("full-basic 0.0.0 (Phase 0 spike)");
    return 0;
}

if (args.Length > 0 && args[0] == "--bigdecimal-spike")
{
    return RunBigDecimalSpike();
}

Console.WriteLine("usage: full-basic [--version | --bigdecimal-spike]");
return 0;

static int RunBigDecimalSpike()
{
    var failures = 0;
    void Check(string label, bool ok)
    {
        Console.WriteLine($"  [{(ok ? "ok" : "FAIL")}] {label}");
        if (!ok) failures++;
    }

    Console.WriteLine("BigDecimal spike:");

    var a = BigDecimal.Parse("1.1");
    var b = BigDecimal.Parse("2.2");
    Check("1.1 + 2.2 == 3.3 exactly", (a + b) == BigDecimal.Parse("3.3"));

    var third = BigDecimal.Divide(BigDecimal.One, BigDecimal.Parse("3"), 10, RoundingMode.MidpointToEven);
    Check("1/3 to 10 digits = 0.3333333333", third == BigDecimal.Parse("0.3333333333"));

    var halfEven1 = BigDecimal.Round(BigDecimal.Parse("0.5"), 0, RoundingMode.MidpointToEven);
    Check("0.5 -> 0 (banker's rounding to even)", halfEven1 == BigDecimal.Zero);

    var halfEven2 = BigDecimal.Round(BigDecimal.Parse("1.5"), 0, RoundingMode.MidpointToEven);
    Check("1.5 -> 2 (banker's rounding to even)", halfEven2 == BigDecimal.Parse("2"));

    var halfUp = BigDecimal.Round(BigDecimal.Parse("0.5"), 0, RoundingMode.MidpointAwayFromZero);
    Check("0.5 -> 1 (half away from zero)", halfUp == BigDecimal.One);

    var big = BigDecimal.Parse("123456789012345678901234567890");
    Check("very large value parses + roundtrips", big.ToString() == "123456789012345678901234567890");

    var verySmall = BigDecimal.Parse("0.000000000000000000001");
    Check("very small value parses + roundtrips", verySmall.ToString() == "0.000000000000000000001");

    Console.WriteLine(failures == 0 ? "spike: PASS" : $"spike: FAIL ({failures} failures)");
    return failures == 0 ? 0 : 1;
}
