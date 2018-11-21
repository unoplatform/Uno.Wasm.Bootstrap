//#define BIT64

using System;
using System.Runtime.InteropServices;
using System.IO;
using System.Diagnostics;
using System.Runtime.CompilerServices;

#if !JSIL
using System.Threading;
using System.Threading.Tasks;
#endif

#if BIT64
using Num = System.Double;
#else
using Num = System.Single;
#endif

namespace RayTraceBenchmark
{
	// ==============================================
	// Main Benchmark Code
	// ==============================================
	struct Vec3
	{
		public Num X, Y, Z;

		public static readonly Vec3 Zero = new Vec3();

		public Vec3(Num x, Num y, Num z)
		{
			X = x;
			Y = y;
			Z = z;
		}

		public static Vec3 operator+(Vec3 p1, Vec3 p2)
		{
			p1.X += p2.X;
			p1.Y += p2.Y;
			p1.Z += p2.Z;
			return p1;
		}

		public static Vec3 operator-(Vec3 p1, Vec3 p2)
		{
			p1.X -= p2.X;
			p1.Y -= p2.Y;
			p1.Z -= p2.Z;
			return p1;
		}

		public static Vec3 operator-(Vec3 p1)
		{
			p1.X = -p1.X;
			p1.Y = -p1.Y;
			p1.Z = -p1.Z;
			return p1;
		}

		public static Vec3 operator*(Vec3 p1, Vec3 p2)
		{
			p1.X *= p2.X;
			p1.Y *= p2.Y;
			p1.Z *= p2.Z;
			return p1;
		}

		public static Vec3 operator*(Vec3 p1, Num p2)
		{
			p1.X *= p2;
			p1.Y *= p2;
			p1.Z *= p2;
			return p1;
		}

		public static Vec3 operator*(Num p1, Vec3 p2)
		{
			p2.X = p1 * p2.X;
			p2.Y = p1 * p2.Y;
			p2.Z = p1 * p2.Z;
			return p2;
		}

		public static Vec3 operator/(Vec3 p1, Vec3 p2)
		{
			p1.X /= p2.X;
			p1.Y /= p2.Y;
			p1.Z /= p2.Z;
			return p1;
		}

		public static Vec3 operator/(Vec3 p1, Num p2)
		{
			p1.X /= p2;
			p1.Y /= p2;
			p1.Z /= p2;
			return p1;
		}

		public static Num Dot(Vec3 v1, Vec3 v2)
		{
			return (v1.X*v2.X) + (v1.Y*v2.Y) + (v1.Z*v2.Z);
		}

		public static Num Magnitude(Vec3 v)
		{
			return (Num)Math.Sqrt((v.X*v.X) + (v.Y*v.Y) + (v.Z*v.Z));
		}

		public static Vec3 Normalize(Vec3 v)
		{
			return v / (Num)Math.Sqrt((v.X*v.X) + (v.Y*v.Y) + (v.Z*v.Z));
		}
	}

	struct Ray
	{
		public Vec3 Org;
		public Vec3 Dir;
	}

	class Sphere
	{
		public Vec3 Center;
		public Num Radius;
		public Vec3 Color;
		public Num Reflection;
		public Num Transparency;

		public Sphere(Vec3 c, Num r, Vec3 clr, Num refl = 0, Num trans = 0)
		{
			Center = c;
			Radius = r;
			Color = clr;
			Reflection = refl;
			Transparency = trans;
		}
		
		public static Vec3 Normal(Sphere sphere, Vec3 pos)
		{
			return Vec3.Normalize(pos - sphere.Center);
		}

		public static bool Intersect(Sphere sphere, Ray ray)
		{
			var l = sphere.Center - ray.Org;
			var a = Vec3.Dot(l, ray.Dir);
			if (a < 0)              // opposite direction
				return false;

			var b2 = Vec3.Dot(l, l) - (a * a);
			var r2 = sphere.Radius * sphere.Radius;
			if (b2 > r2)            // perpendicular > r
				return false;

			return true;
		}

		public static bool Intersect(Sphere sphere, Ray ray, out Num distance)
		{
			distance = 0;

			var l = sphere.Center - ray.Org;
			var a = Vec3.Dot(l, ray.Dir);
			if (a < 0)              // opposite direction
				return false;

			var b2 = Vec3.Dot(l, l) - (a * a);
			var r2 = sphere.Radius * sphere.Radius;
			if (b2 > r2)            // perpendicular > r
				return false;

			var c = (Num)Math.Sqrt(r2 - b2);
			var near = a - c;
			var far  = a + c;
			distance = (near < 0) ? far : near;
			// near < 0 means ray starts inside
			return true;
		}
	}

	class Light
	{
		public Vec3 Position;
		public Vec3 Color;

		public Light(Vec3 position, Vec3 color)
		{
			Position = position;
			Color = color;
		}
	}

	class Scene
	{
		public Sphere[] Objects;
		public Light[] Lights;
	}

	static class Benchmark
	{
		public const int Width = 1280;
		public const int Height = 720;
		private const Num fov = 45;
		private const int maxDepth = 6;
		private const Num PI = (Num)Math.PI;

		private static Vec3 trace (Ray ray, Scene scene, int depth)
		{
			var nearest = Num.MaxValue;
			Sphere obj = null;

			// search the scene for nearest intersection
			foreach(var o in scene.Objects)
			{
				var distance = Num.MaxValue;
				if (Sphere.Intersect(o, ray, out distance))
				{
					if (distance < nearest)
					{
						nearest = distance;
						obj = o;
					}
				}
			}

			if (obj == null) return Vec3.Zero;

			var point_of_hit = ray.Org + (ray.Dir * nearest);
			var normal = Sphere.Normal(obj, point_of_hit);
			bool inside = false;

			if (Vec3.Dot(normal, ray.Dir) > 0)
			{
				inside = true;
				normal = -normal;
			}

			Vec3 color = Vec3.Zero;
			var reflection_ratio = obj.Reflection;

			foreach(var l in scene.Lights)
			{
				var light_direction = Vec3.Normalize(l.Position - point_of_hit);
				Ray r;
				r.Org = point_of_hit + (normal * 1e-5f);
				r.Dir = light_direction;

				// go through the scene check whether we're blocked from the lights
				bool blocked = false;
				foreach (var o in scene.Objects)
				{
					if (Sphere.Intersect(o, r))
					{
						blocked = true;
						break;
					}
				}

				if (!blocked)
				{
					color += l.Color
						* Math.Max(0, Vec3.Dot(normal, light_direction))
						* obj.Color
						* (1.0f - reflection_ratio);
				}
			}

			var rayNormDot = Vec3.Dot(ray.Dir, normal);
			Num facing = Math.Max(0, -rayNormDot);
			Num fresneleffect = reflection_ratio + ((1 - reflection_ratio) * (Num)Math.Pow((1 - facing), 5));

			// compute reflection
			if (depth < maxDepth && reflection_ratio > 0)
			{
				var reflection_direction = ray.Dir + (normal * 2 * rayNormDot * (-1.0f));
				Ray r;
				r.Org = point_of_hit + (normal * 1e-5f);
				r.Dir = reflection_direction;
				var reflection = trace(r, scene, depth + 1);
				color += reflection * fresneleffect;
			}

			// compute refraction
			if (depth < maxDepth && (obj.Transparency > 0))
			{
				var ior = 1.5f;
				var CE = Vec3.Dot(ray.Dir, normal) * (-1.0f);
				ior = inside ? (1.0f) / ior : ior;
				var eta = (1.0f) / ior;
				var GF = (ray.Dir + normal * CE) * eta;
				var sin_t1_2 = 1 - (CE * CE);
				var sin_t2_2 = sin_t1_2 * (eta * eta);
				if (sin_t2_2 < 1)
				{
					var GC = normal * (Num)Math.Sqrt(1 - sin_t2_2);
					var refraction_direction = GF - GC;
					Ray r;
					r.Org = point_of_hit - (normal * 1e-4f);
					r.Dir = refraction_direction;
					var refraction = trace(r, scene, depth + 1);
					color += refraction * (1 - fresneleffect) * obj.Transparency;
				}
			}
			return color;
		}

		public static byte[] Render(Scene scene, byte[] pixels)
		{
			var eye = Vec3.Zero;
			Num h = (Num)Math.Tan(((fov / 360) * (2 * PI)) / 2) * 2;
			Num w = h * Width / Height;

			for (int y = 0; y != Height; ++y)
			{
				for (int x = 0; x != Width; ++x)
				{
					Num xx = x, yy = y, ww = Width, hh = Height;
					Vec3 dir;
					dir.X = ((xx - (ww / 2.0f)) / ww)  * w;
					dir.Y = (((hh/2.0f) - yy) / hh) * h;
					dir.Z = -1.0f;
					dir = Vec3.Normalize(dir);

					Ray r;
					r.Org = eye;
					r.Dir = dir;
					var pixel = trace(r, scene, 0);
					int i = (x*3) + (y*Width*3);
					pixels[i] = (byte)Math.Min(pixel.X * 255, 255);
					pixels[i+1] = (byte)Math.Min(pixel.Y * 255, 255);
					pixels[i+2] = (byte)Math.Min(pixel.Z * 255, 255);
				}
			}

			return pixels;
		}
	}

	// ==============================================
	// Prep Code
	// ==============================================
	#if UWP || WIN8 || WP8 || WP7 || ANDROID || IOS || VITA
	static class Console
	{
		public delegate void WriteLineCallbackMethod(string value);
		public static WriteLineCallbackMethod WriteLineCallback;

		public static void WriteLine(string value)
		{
			if (WriteLineCallback != null) WriteLineCallback(value);
		}
	}

	#if !WP8 && !WP7 && !ANDROID && !IOS && !VITA
	static class Thread
	{
		public static void Sleep(int milli)
		{
			using (var _event = new ManualResetEvent(false))
			{
				_event.WaitOne(milli);
			}
		}
	}
	#endif
	#endif

	static class BenchmarkMain
	{
		#if WIN32
		[StructLayout(LayoutKind.Sequential)]
		public struct TimeCaps
		{
			public uint wPeriodMin;
			public uint wPeriodMax;
		}

		private static TimeCaps caps;

		[DllImport("winmm.dll", EntryPoint="timeGetDevCaps", SetLastError=true)]
		public static extern uint TimeGetDevCaps(ref TimeCaps timeCaps, uint sizeTimeCaps);

		[DllImport("winmm.dll", EntryPoint="timeBeginPeriod", SetLastError=true)]
		public static extern uint TimeBeginPeriod(uint uMilliseconds);

		[DllImport("winmm.dll", EntryPoint="timeEndPeriod", SetLastError=true)]
		public static extern uint TimeEndPeriod(uint uMilliseconds);

		public static void Win32OptimizedStopwatch()
		{
			caps = new TimeCaps();
			if (TimeGetDevCaps(ref caps, (uint)System.Runtime.InteropServices.Marshal.SizeOf(caps)) != 0)
			{
				Console.WriteLine("StopWatch: TimeGetDevCaps failed");
			}
			
			if (TimeBeginPeriod(caps.wPeriodMin) != 0)
			{
				Console.WriteLine("StopWatch: TimeBeginPeriod failed");
			}
		}

		public static void Win32EndOptimizedStopwatch()
		{
			if (TimeEndPeriod(caps.wPeriodMin) != 0)
			{
				Console.WriteLine("StopWatch: TimeEndPeriod failed");
			}
		}
		#endif

		#if UWP || WIN8 || WP8 || WP7 || ANDROID || IOS || VITA
		public delegate void SaveImageCallbackMethod(byte[] data);
		public static SaveImageCallbackMethod SaveImageCallback;
		#else
		static void Main(string[] args)
		{
			Start();
		}
		#endif

		#if JSIL
		public static byte[] Start()
		#else
		public static async Task Start()
		#endif
		{
			// create objects
			var scene = new Scene();
			scene.Objects = new Sphere[]
			{
				new Sphere(new Vec3(0.0f, -10002.0f, -20.0f), 10000, new Vec3(.8f, .8f, .8f)),
				new Sphere(new Vec3(0.0f, 2.0f, -20.0f), 4, new Vec3(.8f, .5f, .5f), 0.5f),
				new Sphere(new Vec3(5.0f, 0.0f, -15.0f), 2, new Vec3(.3f, .8f, .8f), 0.2f),
				new Sphere(new Vec3(-5.0f, 0.0f, -15.0f), 2, new Vec3(.3f, .5f, .8f), 0.2f),
				new Sphere(new Vec3(-2.0f, -1.0f, -10.0f), 1, new Vec3(.1f, .1f, .1f), 0.1f, 0.8f)
			};

			scene.Lights = new Light[]{new Light(new Vec3(-10, 20, 30), new Vec3(2, 2, 2))};
			
			int pixelsLength = Benchmark.Width * Benchmark.Height * 3;
			byte[] pixels = new byte[pixelsLength];

			#if !__WASM__
			// give the system a little time
			#if !JSIL
			GC.Collect();
			Console.WriteLine("Give the system a little time...");
			await Task.Delay(2000);
			#endif
			Console.WriteLine("Starting test...");
			await Task.Yield();
			#endif

			// run test
			#if WIN32
			Win32OptimizedStopwatch();
			#endif

			var watch = new Stopwatch();
			watch.Start();
			var data = Benchmark.Render(scene, pixels);
			watch.Stop();
			Console.WriteLine("Sec: " + (watch.ElapsedMilliseconds / 1000d));

			#if WIN32
			Win32EndOptimizedStopwatch();
			#endif

			// save image
			#if UWP || WIN8 || WP8 || WP7 || ANDROID || IOS || VITA
			if (SaveImageCallback != null) SaveImageCallback(data);
			#elif !JSIL
			Console.ReadLine();
			using (var file = new FileStream("Image.raw", FileMode.Create, FileAccess.Write))
			using (var writer = new BinaryWriter(file))
			{
				for (int i = 0; i != pixelsLength; ++i)
				{
					file.WriteByte(data[i]);
				}
			}
			#else
			return pixels;
			#endif
		}

		public static byte[] ConvertRGBToBGRA(byte[] rgb)
		{
			var rgba = new byte[(rgb.Length/3) * 4];
			for (int i = 0, i2 = 0; i != rgb.Length; i += 3, i2 += 4)
			{
				rgba[i2] = rgb[i+2];
				rgba[i2+1] = rgb[i+1];
				rgba[i2+2] = rgb[i];
				rgba[i2+3] = 255;
			}

			return rgba;
		}

		public static int[] ConvertRGBToBGRAInt(byte[] rgb)
		{
			var rgba = new int[rgb.Length/3];
			for (int i = 0, i2 = 0; i != rgb.Length; i += 3, i2 += 1)
			{
				int color = rgb[i+2];
				color |= rgb[i+1] << 8;
				color |= rgb[i] << 16;
				color |= 255 << 24;
				rgba[i2] = color;
			}

			return rgba;
		}
	}
}
