using blob_demo.Services;
using Microsoft.AspNetCore.Mvc;

namespace blob_demo.Controllers;

public class OrdersController : Controller
{
    private readonly IOrderService _orders;

    public OrdersController(IOrderService orders) => _orders = orders;

    // GET /Orders — order list + queue depth, optional region filter (FR-1.3).
    [HttpGet]
    public async Task<IActionResult> Index(string? region, CancellationToken ct)
    {
        ViewData["Region"] = region;
        var model = await _orders.GetPageAsync(region, ct);
        return View(model);
    }

    // POST /Orders/Create — writes to Table + enqueues, then returns immediately.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(
        string region, string customerName, string product, int quantity, double total, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(region) || string.IsNullOrWhiteSpace(product))
        {
            TempData["Error"] = "Region and Product are required.";
            return RedirectToAction(nameof(Index));
        }
        if (quantity <= 0)
        {
            TempData["Error"] = "Quantity must be greater than zero.";
            return RedirectToAction(nameof(Index));
        }

        var order = await _orders.CreateOrderAsync(
            new CreateOrderRequest(region, customerName, product, quantity, total), ct);

        TempData["Success"] = $"Order {order.OrderId[..8]}… accepted and queued for processing.";
        return RedirectToAction(nameof(Index));
    }
}
