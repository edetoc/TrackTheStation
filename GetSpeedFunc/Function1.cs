using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace GetSpeedFunc
{
    public static class Function1
    {
        [FunctionName("Function1")]
        public static IActionResult Run(
          [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = null)] HttpRequest req,
          ILogger log)
        {

            string x = req.Query["x"];

            string y = req.Query["y"];

            string z = req.Query["z"];

            return (Double.TryParse(x, out var xNumber) && Double.TryParse(y, out var yNumber) && Double.TryParse(z, out var zNumber))
           ? (ActionResult)new OkObjectResult(3600 * Math.Sqrt(Math.Pow(Math.Abs(xNumber), 2) +
                           Math.Pow(Math.Abs(yNumber), 2) +
                               Math.Pow(Math.Abs(zNumber), 2)))
           : new BadRequestObjectResult("HINT: Invalid values passed AND/OR invalid parameter names.");
        }

    }
}
