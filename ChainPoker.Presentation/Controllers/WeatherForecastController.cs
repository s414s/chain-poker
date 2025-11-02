using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Immutable;
using System.Text;

namespace ChainPoker.Presentation.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class WeatherForecastController : ControllerBase
    {
        private static readonly string[] Summaries = new[]
        {
            "Freezing", "Bracing", "Chilly", "Cool", "Mild", "Warm", "Balmy", "Hot", "Sweltering", "Scorching"
        };

        private readonly ILogger<WeatherForecastController> _logger;

        public WeatherForecastController(ILogger<WeatherForecastController> logger)
        {
            _logger = logger;
        }

        [HttpGet(Name = "GetWeatherForecast")]
        public IEnumerable<WeatherForecast> Get()
        {
            return Enumerable.Range(1, 5).Select(index => new WeatherForecast
            {
                Date = DateOnly.FromDateTime(DateTime.Now.AddDays(index)),
                TemperatureC = Random.Shared.Next(-20, 55),
                Summary = Summaries[Random.Shared.Next(Summaries.Length)]
            })
            .ToArray();
        }

        [HttpPost]
        [RequestSizeLimit(50_000_000)] // 50 MB, adjust
        public async Task<IActionResult> Upload([FromForm] IFormFile file)
        {
            if (file == null || file.Length == 0)
                return BadRequest("No file uploaded.");

            //var x = file.OpenReadStream().Seek(0, SeekOrigin.Begin);
            var x = file.OpenReadStream();

            await using var s = file.OpenReadStream();

            var encoding = Encoding.Latin1; // or Encoding.GetEncoding("iso-8859-1")
            using var reader = new StreamReader(s, Encoding.UTF8);
            if (reader == null)
                return NoContent();

            var content = reader.ReadToEnd();
            var span = content.AsSpan();

            foreach (var range in span.Split(';'))
            {
                ReadOnlySpan<char> registry = span[range]; // actual text slice

                if (registry.IsEmpty)
                    continue;

                var isPrefix = registry is ['A', 'B', ..] or ['C', 'D', ..];
                var isDigit = registry is [>= '0' and <= '9', ..];
                var isLetterDash = registry is [>= 'A' and <= 'Z', '-', ..];
                var isMirror = registry is [var a, .., var b] && a == b;
                var isMirrorIgnoring = registry is [var c, .., _, var d] && c == d;
                //var isTwoFields = registry.Split(',') is [_, _];
                var isShortA = registry is ['A', ..] and { Length: < 5 };

                var isHex = registry is ['0', 'x', .. var digits]
                    && digits.ToImmutableArray().All(c =>
                        (c >= '0' && c <= '9') ||
                        (c >= 'A' && c <= 'F'));

                bool myResult = registry switch
                {
                    ['A', '|', .. var qtyChars]
                        when int.TryParse(qtyChars, out var qty)
                            => true, // handle(qtyChars)

                    ['B', '|', .. var qtyChars]
                        when int.TryParse(qtyChars, out var qty)
                            => true, // handle(qtyChars)

                    _ => false
                };

                var isLike = registry switch
                {
                    ['A', ..] => true,
                    ['B', .., 'C'] => true,
                    _ => false,
                };

                var result = registry[0] switch
                {
                    'A' => ProcessRegistry(registry),
                    'B' => ProcessRegistry(registry),
                    >= '0' and <= '9' => ProcessRegistry(registry),
                    _ => ProcessRegistry(registry)
                };
            }

            return Ok(new { file.FileName, file.Length });
        }

        static async IAsyncEnumerable<ReadOnlyMemory<byte>> ReadChunksAsync(Stream s)
        {
            byte[] buffer = new byte[8192];
            int read;
            while ((read = await s.ReadAsync(buffer.AsMemory())) > 0)
            {
                yield return buffer.AsMemory(0, read);
            }
        }

        static bool ProcessRegistry(ReadOnlySpan<char> span)
        {
            var test = span.ToString().Split('/');
            var myValue = test[0];

            var integer = int.Parse(span);


            var splitter = span.Split('/');

            ReadOnlySpan<char> codeSpan = default;
            ReadOnlySpan<char> qtySpan = default;

            int partIndex = 0;

            foreach (var range in splitter)
            {
                ReadOnlySpan<char> part = span[range];

                if (part.IsEmpty)
                    continue;

                if (partIndex == 0)
                    codeSpan = part;   // first field → code

                if (partIndex == 1)
                {
                    qtySpan = part;    // second field → quantity
                    break;             // we already have what we need
                }

                partIndex++;
            }


            var e = splitter.GetEnumerator();

            // 1) code
            if (!e.MoveNext()) throw new Exception();
            var code2 = span[e.Current];
            if (code2.IsEmpty) return false;

            // 2) quantity
            if (!e.MoveNext()) throw new Exception();
            var qtySpan2 = span[e.Current];
            if (qtySpan2.IsEmpty) return false;

            if (!int.TryParse(qtySpan, out var qty))
                return false;

            return true;
        }

    }
}
