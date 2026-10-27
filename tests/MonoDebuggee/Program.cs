using LocalDebug.Demo;

Thread.CurrentThread.Name = "Demo host";
// Ensure the target type is loaded before reporting readiness.
Console.WriteLine($"READY:{CounterMath.ComputeNext(21)}");
bool incrementing = args.Contains("--incrementing");
int input = 21;
while (true)
{
    int value = CounterMath.ComputeNext(input);
    Console.WriteLine($"TICK:{value}");
    if (incrementing) input++;
    Thread.Sleep(100);
}
