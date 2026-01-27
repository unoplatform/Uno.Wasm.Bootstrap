using System;
using Newtonsoft.Json;

namespace Uno.Wasm.Tests.MixedModeProfile
{
	class Program
	{
		static void Main(string[] args)
		{
			// Use Newtonsoft.Json to ensure its methods are in the AOT profile
			var testObject = new { Message = "Mixed Mode Profile Test", Value = 42 };
			var json = JsonConvert.SerializeObject(testObject);
			Console.WriteLine($"Test output: {json}");
			
			// Deserialize to ensure more methods are used
			var deserialized = JsonConvert.DeserializeObject<dynamic>(json);
			Console.WriteLine($"Deserialized: {deserialized.Message}");
		}
	}
}
