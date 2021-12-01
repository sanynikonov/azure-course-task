using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.Web.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Azure.Messaging.ServiceBus;

namespace Microsoft.eShopWeb.Web.Pages.Basket
{
    [Authorize]
    public class CheckoutModel : PageModel
    {
        private readonly IBasketService _basketService;
        private readonly SignInManager<ApplicationUser> _signInManager;
        private readonly IOrderService _orderService;
        private string _username = null;
        private readonly IBasketViewModelService _basketViewModelService;
        private readonly IAppLogger<CheckoutModel> _logger;

        public CheckoutModel(IBasketService basketService,
            IBasketViewModelService basketViewModelService,
            SignInManager<ApplicationUser> signInManager,
            IOrderService orderService,
            IAppLogger<CheckoutModel> logger)
        {
            _basketService = basketService;
            _signInManager = signInManager;
            _orderService = orderService;
            _basketViewModelService = basketViewModelService;
            _logger = logger;
        }

        public BasketViewModel BasketModel { get; set; } = new BasketViewModel();

        public async Task OnGet()
        {
            await SetBasketModelAsync();
        }

        public async Task<IActionResult> OnPost(IEnumerable<BasketItemViewModel> items)
        {
            try
            {
                await SetBasketModelAsync();

                if (!ModelState.IsValid)
                {
                    return BadRequest();
                }

                var itemsArray = items.ToArray();
                var shippingAddress = new Address("123 Main St.", "Kent", "OH", "United States", "44240");
                
                var updateModel = itemsArray.ToDictionary(b => b.Id.ToString(), b => b.Quantity);
                await _basketService.SetQuantities(BasketModel.Id, updateModel);
                await _orderService.CreateOrderAsync(BasketModel.Id, shippingAddress);
                await DeliveryOrder(itemsArray, shippingAddress);
                await ReserveItems(itemsArray, shippingAddress);
                await _basketService.DeleteBasketAsync(BasketModel.Id);
                
            }
            catch (EmptyBasketOnCheckoutException emptyBasketOnCheckoutException)
            {
                //Redirect to Empty Basket page
                _logger.LogWarning(emptyBasketOnCheckoutException.Message);
                return RedirectToPage("/Basket/Index");
            }

            return RedirectToPage("Success");
        }

        private async Task SetBasketModelAsync()
        {
            if (_signInManager.IsSignedIn(HttpContext.User))
            {
                BasketModel = await _basketViewModelService.GetOrCreateBasketForUser(User.Identity.Name);
            }
            else
            {
                GetOrSetBasketCookieAndUserName();
                BasketModel = await _basketViewModelService.GetOrCreateBasketForUser(_username);
            }
        }

        private void GetOrSetBasketCookieAndUserName()
        {
            if (Request.Cookies.ContainsKey(Constants.BASKET_COOKIENAME))
            {
                _username = Request.Cookies[Constants.BASKET_COOKIENAME];
            }
            if (_username != null) return;

            _username = Guid.NewGuid().ToString();
            var cookieOptions = new CookieOptions();
            cookieOptions.Expires = DateTime.Today.AddYears(10);
            Response.Cookies.Append(Constants.BASKET_COOKIENAME, _username, cookieOptions);
        }

        private async Task DeliveryOrder(IEnumerable<BasketItemViewModel> items, Address address)
        {
            var order = new OrderDetails
            {
                ShippingAddress = address,
                Items = items.Select(i => new OrderedItem { ItemId = i.Id, Quantity = i.Quantity }).ToArray(),
                FinalPrice = Math.Round(items.Sum(x => x.UnitPrice * x.Quantity), 2)
            };

            var client = new HttpClient();

            var requestBody = JsonSerializer.Serialize(order);

            Console.WriteLine(requestBody);

            var response = await client.PostAsync("https://sany-web-function.azurewebsites.net/api/DeliveryOrder",
                new StringContent(requestBody));
        }

        private class OrderDetails
        {
            public decimal FinalPrice { get; set; }
            public Address ShippingAddress { get; set; }
            public IEnumerable<OrderedItem> Items { get; set; }
        }

        // connection string to your Service Bus namespace
        static string connectionString = "Endpoint=sb://sany-web-broker.servicebus.windows.net/;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=y4sBkr0L4EvyzVWhxwCzPSKeFZwbEuo5GV5SpNIMRz4=";

        // name of your Service Bus queue
        static string queueName = "orders";

        private async Task ReserveItems(IEnumerable<BasketItemViewModel> items, Address address)
        {
            var order = new OrderDetails
            {
                ShippingAddress = address,
                Items = items.Select(i => new OrderedItem { ItemId = i.Id, Quantity = i.Quantity }).ToArray(),
                FinalPrice = Math.Round(items.Sum(x => x.UnitPrice * x.Quantity), 2)
            };

            var requestBody = JsonSerializer.Serialize(order);

            var client = new ServiceBusClient(connectionString);
            var sender = client.CreateSender(queueName);

            await sender.SendMessageAsync(new ServiceBusMessage(requestBody));
        }
    }
}
