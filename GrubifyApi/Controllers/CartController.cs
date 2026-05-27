using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using GrubifyApi.Models;

namespace GrubifyApi.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class CartController : ControllerBase
    {
        private readonly IMemoryCache _cache;

        // Maximum items allowed per cart (realistic limit for food delivery)
        private const int MaxItemsPerCart = 20;

        // Cache entry options: shorter expiration + absolute cap to prevent memory buildup
        // Hardened after OOM incident (May 26 2026)
        private static readonly MemoryCacheEntryOptions CacheOptions = new()
        {
            SlidingExpiration = TimeSpan.FromMinutes(15),
            AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(2),
            Size = 1
        };

        // Static readonly food items list — created once, reused on every request
        private static readonly List<FoodItem> FoodItems = new()
        {
            new FoodItem { Id = 1, Name = "Margherita Pizza", Price = 16.99m, ImageUrl = "https://images.unsplash.com/photo-1604382354936-07c5d9983bd3?w=400&h=300&fit=crop", RestaurantId = 1 },
            new FoodItem { Id = 2, Name = "Chicken Alfredo", Price = 19.99m, ImageUrl = "https://images.unsplash.com/photo-1621996346565-e3dbc353d2e5?w=400&h=300&fit=crop", RestaurantId = 1 },
            new FoodItem { Id = 3, Name = "Caesar Salad", Price = 12.99m, ImageUrl = "https://images.unsplash.com/photo-1546793665-c74683f339c1?w=400&h=300&fit=crop", RestaurantId = 1 },
            new FoodItem { Id = 4, Name = "California Roll", Price = 14.99m, ImageUrl = "https://images.unsplash.com/photo-1579584425555-c3ce17fd4351?w=400&h=300&fit=crop", RestaurantId = 2 },
            new FoodItem { Id = 5, Name = "Spicy Tuna Roll", Price = 16.99m, ImageUrl = "https://images.unsplash.com/photo-1617196034796-73dfa7b1fd56?w=400&h=300&fit=crop", RestaurantId = 2 },
            new FoodItem { Id = 6, Name = "Chicken Teriyaki Bowl", Price = 18.99m, ImageUrl = "https://images.unsplash.com/photo-1546069901-eacef0df6022?w=400&h=300&fit=crop", RestaurantId = 2 },
            new FoodItem { Id = 7, Name = "Chicken Tikka Masala", Price = 17.99m, ImageUrl = "https://images.unsplash.com/photo-1565557623262-b51c2513a641?w=400&h=300&fit=crop", RestaurantId = 3 },
            new FoodItem { Id = 8, Name = "Vegetable Biryani", Price = 15.99m, ImageUrl = "https://images.unsplash.com/photo-1563379091339-03246963d17a?w=400&h=300&fit=crop", RestaurantId = 3 },
            new FoodItem { Id = 9, Name = "Garlic Naan", Price = 4.99m, ImageUrl = "https://images.unsplash.com/photo-1601050690597-df0568f70950?w=400&h=300&fit=crop", RestaurantId = 3 },
            new FoodItem { Id = 10, Name = "Classic Cheeseburger", Price = 13.99m, ImageUrl = "https://images.unsplash.com/photo-1568901346375-23c9450c58cd?w=400&h=300&fit=crop", RestaurantId = 4 },
            new FoodItem { Id = 11, Name = "Crispy Chicken Sandwich", Price = 15.99m, ImageUrl = "https://images.unsplash.com/photo-1606755962773-d324e9a13086?w=400&h=300&fit=crop", RestaurantId = 4 },
            new FoodItem { Id = 12, Name = "Sweet Potato Fries", Price = 6.99m, ImageUrl = "https://images.unsplash.com/photo-1573080496219-bb080dd4f877?w=400&h=300&fit=crop", RestaurantId = 4 },
            new FoodItem { Id = 13, Name = "Quinoa Buddha Bowl", Price = 14.99m, ImageUrl = "https://images.unsplash.com/photo-1512621776951-a57141f2eefd?w=400&h=300&fit=crop", RestaurantId = 5 },
            new FoodItem { Id = 14, Name = "Acai Berry Smoothie", Price = 8.99m, ImageUrl = "https://images.unsplash.com/photo-1553530666-ba11a7da3888?w=400&h=300&fit=crop", RestaurantId = 5 },
            new FoodItem { Id = 15, Name = "Grilled Salmon Salad", Price = 18.99m, ImageUrl = "https://images.unsplash.com/photo-1540420773420-3366772f4999?w=400&h=300&fit=crop", RestaurantId = 5 }
        };

        public CartController(IMemoryCache cache)
        {
            _cache = cache;
        }

        private static string CartKey(string userId) => $"cart:{userId}";

        [HttpGet("{userId}")]
        public ActionResult<Cart> GetCart(string userId)
        {
            var cart = _cache.GetOrCreate(CartKey(userId), entry =>
            {
                entry.SetOptions(CacheOptions);
                return new Cart { UserId = userId };
            });

            return Ok(cart);
        }

        [HttpPost("{userId}/items")]
        public ActionResult<Cart> AddItemToCart(string userId, [FromBody] AddCartItemRequest request)
        {
            // Input validation
            if (request.Quantity < 1 || request.Quantity > 99)
            {
                return BadRequest("Quantity must be between 1 and 99.");
            }

            var cart = _cache.GetOrCreate(CartKey(userId), entry =>
            {
                entry.SetOptions(CacheOptions);
                return new Cart { UserId = userId };
            })!;

            // Enforce per-cart item limit
            if (cart.Items.Count >= MaxItemsPerCart)
            {
                return BadRequest($"Cart is full. Maximum {MaxItemsPerCart} items allowed.");
            }

            var existingItem = cart.Items.FirstOrDefault(i => i.FoodItemId == request.FoodItemId);

            if (existingItem != null)
            {
                existingItem.Quantity += request.Quantity;
                existingItem.SpecialInstructions = request.SpecialInstructions;
            }
            else
            {
                var newItem = new CartItem
                {
                    Id = cart.Items.Count + 1,
                    FoodItemId = request.FoodItemId,
                    FoodItem = GetFoodItemById(request.FoodItemId),
                    Quantity = request.Quantity,
                    SpecialInstructions = request.SpecialInstructions
                };
                cart.Items.Add(newItem);
            }

            // Refresh the cache entry to reset the sliding expiration
            _cache.Set(CartKey(userId), cart, CacheOptions);

            return Ok(cart);
        }

        [HttpPut("{userId}/items/{itemId}")]
        public ActionResult<Cart> UpdateCartItem(string userId, int itemId, [FromBody] UpdateCartItemRequest request)
        {
            if (!_cache.TryGetValue(CartKey(userId), out Cart? cart) || cart == null)
            {
                return NotFound("Cart not found");
            }

            var item = cart.Items.FirstOrDefault(i => i.Id == itemId);

            if (item == null)
            {
                return NotFound("Item not found in cart");
            }

            if (request.Quantity < 1 || request.Quantity > 99)
            {
                return BadRequest("Quantity must be between 1 and 99.");
            }

            item.Quantity = request.Quantity;
            item.SpecialInstructions = request.SpecialInstructions;

            _cache.Set(CartKey(userId), cart, CacheOptions);

            return Ok(cart);
        }

        [HttpDelete("{userId}/items/{itemId}")]
        public ActionResult<Cart> RemoveItemFromCart(string userId, int itemId)
        {
            if (!_cache.TryGetValue(CartKey(userId), out Cart? cart) || cart == null)
            {
                return NotFound("Cart not found");
            }

            var item = cart.Items.FirstOrDefault(i => i.Id == itemId);

            if (item == null)
            {
                return NotFound("Item not found in cart");
            }

            cart.Items.Remove(item);
            _cache.Set(CartKey(userId), cart, CacheOptions);

            return Ok(cart);
        }

        [HttpDelete("{userId}")]
        public ActionResult ClearCart(string userId)
        {
            _cache.Remove(CartKey(userId));
            return Ok();
        }

        // Helper method to get food item from the static list (no per-request allocation)
        private static FoodItem GetFoodItemById(int foodItemId)
        {
            return FoodItems.FirstOrDefault(f => f.Id == foodItemId) ?? new FoodItem();
        }
    }

    public class AddCartItemRequest
    {
        public int FoodItemId { get; set; }
        public int Quantity { get; set; }
        public string SpecialInstructions { get; set; } = string.Empty;
    }

    public class UpdateCartItemRequest
    {
        public int Quantity { get; set; }
        public string SpecialInstructions { get; set; } = string.Empty;
    }
}
