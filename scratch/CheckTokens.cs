using System;
using System.Reflection;
using Microsoft.Agents.AI;

class Program
{
    static void Main()
    {
        var type = typeof(AgentResponse).GetProperty("Usage").PropertyType;
        foreach (var prop in type.GetProperties())
        {
            Console.WriteLine($"{prop.PropertyType.Name} {prop.Name}");
        }
    }
}
