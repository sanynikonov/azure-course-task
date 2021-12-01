using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using System.Web.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using JsonSerializer = System.Text.Json.JsonSerializer;

namespace DeliveryOrderFunc
{
    public static class Function1
    {
        [FunctionName("DeliveryOrder")]
        public static async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = null)] HttpRequest req,
            ILogger log)
        {
            log.LogInformation("C# HTTP trigger function processed a request.");

            var order = await JsonSerializer.DeserializeAsync<OrderDetails>(req.Body);
            order.Id = Guid.NewGuid().ToString();

            var endpointUri = "https://sanynikonov.documents.azure.com:443/";
            var primaryKey = "6SSmwPpAuzYdtdAkqaSflwxE3vcK3D3EDagT3IN4LPCsklAbfdAfw0antOAithdgsFDzWl2KUlVR1dftgOhKIw==";
            var databaseId = "SanyWebShop";
            var containerId = "Orders";

            try
            {
                var cosmosClient = new CosmosClient(endpointUri, primaryKey);

                var database = await cosmosClient.CreateDatabaseIfNotExistsAsync(databaseId);
                var container = await database.Database.CreateContainerIfNotExistsAsync(containerId, "/Details");

                await container.Container.CreateItemAsync(order);
            }
            catch (Exception e)
            {
                log.LogError(e.Message);
                return new InternalServerErrorResult();
            }
            
            return new OkResult();
        }

        public class OrderedItem
        {
            public int ItemId { get; set; }
            public int Quantity { get; set; }
        }

        public class OrderDetails
        {
            [JsonProperty("id")]
            public string Id { get; set; }
            public decimal FinalPrice { get; set; }
            public Address ShippingAddress { get; set; }
            public IEnumerable<OrderedItem> Items { get; set; }
        }

        public class Address // ValueObject
        {
            public string Street { get; set; }

            public string City { get; set; }

            public string State { get; set; }

            public string Country { get; set; }

            public string ZipCode { get; set; }
        }
    }
}
