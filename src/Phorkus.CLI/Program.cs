﻿namespace Phorkus.CLI
{
    class Program
    {
        internal static void Main(string[] args)
        {
            var app = new Application();
            app.Start(args);
        }
    }
}