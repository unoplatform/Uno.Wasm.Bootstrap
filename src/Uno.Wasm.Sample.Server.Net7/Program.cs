using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Uno.Wasm.Bootstrap.Server;

namespace Uno.Wasm.Sample.Server.Net7
{
	public class Program
	{
		public static void Main(string[] args)
		{
			var builder = WebApplication.CreateBuilder(args);

			// Add services to the container.

			builder.Services.AddControllers();

			var app = builder.Build();

			// Configure the HTTP request pipeline.

			app.UseAuthorization();

			app.UseUnoFrameworkFiles();

			app.MapControllers();
			app.UseStaticFiles();
			app.MapFallbackToFile("index.html");

			app.Run();
		}
	}
}
