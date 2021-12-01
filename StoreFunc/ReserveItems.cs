using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Azure.Storage.Blobs;
using Microsoft.Azure.WebJobs;
using Microsoft.Extensions.Logging;
using Polly;

namespace StoreFunc
{
    public static class ReserveItems
    {
        [FunctionName("ReserveItems")]
        public static async Task Run([ServiceBusTrigger("orders", Connection = "ServiceBusConnection")]string myQueueItem,
            ILogger log)
        {
            try
            {
                log.LogInformation($"C# ServiceBus queue trigger function processed message: {myQueueItem}");

                var policy = Policy.Handle<Exception>().RetryAsync(3);

                var storageConnection =
                    "DefaultEndpointsProtocol=https;AccountName=sanystorage;AccountKey=nGijCTf2yUDVe/GyrLcP48Q4e4DMfrFVhy/MDOdCnQS+/LCiOerP5JpRBttVH+zgzcQHzjOGtuDwCl046RWu3Q==;EndpointSuffix=core.windows.net";
                var containerName = "orders";

                await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(myQueueItem));

                await policy.ExecuteAsync(async () =>
                {
                    //init storage
                    var blobServiceClient = new BlobServiceClient(storageConnection);
                    var containerClient = blobServiceClient.GetBlobContainerClient(containerName);
                    await containerClient.CreateIfNotExistsAsync();
        
                    //send blob
                    var blobName = $"/order{DateTime.UtcNow:yyyyMMddHHmmssffff}.json";
                    var blobClient = containerClient.GetBlobClient(blobName);
                    await blobClient.UploadAsync(stream);
                });

                log.LogInformation("Blob successfully uploaded");
            }
            catch (Exception e)
            {
                var httpClient = new HttpClient();
                var error = new { e.Message, Order = myQueueItem };
                await httpClient.PostAsync("https://sany-logic-app.azurewebsites.net:443/api/blobuploadfail/triggers/manual/invoke?api-version=2020-05-01-preview&sp=%2Ftriggers%2Fmanual%2Frun&sv=1.0&sig=eufxN4RsxcbHiPsrcLU53wi8x1TanO_luW2PthAjksg",
                    new StringContent(JsonSerializer.Serialize(error)));
                throw;
            }
        }
    }
}
