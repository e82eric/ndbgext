void Meth1()
{
    Meth2();
}
void Meth2()
{
    Meth3();
}
void Meth3()
{
    Thread.Sleep(TimeSpan.FromMinutes(1));
}

for (var i = 0; i < 10; i++)
{
    Task.Run(Meth1);
}
Console.ReadLine();
