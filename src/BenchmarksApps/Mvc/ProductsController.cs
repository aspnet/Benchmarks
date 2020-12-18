using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Certificate;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Mvc
{
    public class ProductsController : ControllerBase
    {
        private static readonly Product[] _fixedProductList = new Product[]
        {
            new(0, "Contoso keyboard 1", 1, 2, 1, 99.95m, 10, 40, 20, false),
            new(0, "Contoso keyboard 2", 1, 2, 1, 99.95m, 10, 40, 20, false),
            new(0, "Contoso keyboard 3", 1, 2, 1, 99.95m, 10, 40, 20, false),
            new(0, "Contoso keyboard 4", 1, 2, 1, 99.95m, 10, 40, 20, false),
            new(0, "Contoso keyboard 5", 1, 2, 1, 99.95m, 10, 40, 20, false),
            new(0, "Contoso keyboard 6", 1, 2, 1, 99.95m, 10, 40, 20, false),
            new(0, "Contoso keyboard 7", 1, 2, 1, 99.95m, 10, 40, 20, false),
            new(0, "Contoso keyboard 8", 1, 2, 1, 99.95m, 10, 40, 20, false),
            new(0, "Contoso keyboard 9", 1, 2, 1, 99.95m, 10, 40, 20, false),
            new(0, "Contoso keyboard 10", 1, 2, 1, 99.95m, 10, 40, 20, false),
            new(0, "Contoso keyboard 11", 1, 2, 1, 99.95m, 10, 40, 20, false),
            new(0, "Contoso keyboard 12", 1, 2, 1, 99.95m, 10, 40, 20, false),
            new(0, "Contoso keyboard 13", 1, 2, 1, 99.95m, 10, 40, 20, false),
            new(0, "Contoso keyboard 14", 1, 2, 1, 99.95m, 10, 40, 20, false),
            new(0, "Contoso keyboard 15", 1, 2, 1, 99.95m, 10, 40, 20, false),
            new(0, "Contoso keyboard 16", 1, 2, 1, 99.95m, 10, 40, 20, false),
            new(0, "Contoso keyboard 17", 1, 2, 1, 99.95m, 10, 40, 20, false),
            new(0, "Contoso keyboard 18", 1, 2, 1, 99.95m, 10, 40, 20, false),
            new(0, "Contoso keyboard 19", 1, 2, 1, 99.95m, 10, 40, 20, false),
            new(0, "Contoso keyboard 20", 1, 2, 1, 99.95m, 10, 40, 20, false),
        };

        [HttpGet("/Products")]
#if AUTHORIZE
        [Authorize]
#endif
        public IEnumerable<Product> GetProducts([FromQuery] Page page)
        {
            return _fixedProductList;
        }

        [HttpGet("/Products/{id}")]
#if AUTHORIZE
        [Authorize]
#endif
        public Product GetProduct([FromRoute] int id)
        {
            return _fixedProductList[0];
        }

        [HttpPost("/Products")]
#if AUTHORIZE
        [Authorize]
#endif
        public ActionResult<Product> AddProduct([FromBody] Product product)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest();
            }
            else
            {
                return CreatedAtAction(nameof(AddProduct), product);
            }
        }

        [HttpPut("/Products/{id}")]
#if AUTHORIZE
        [Authorize]
#endif
        public ActionResult<Product> UpdateProduct([FromRoute] int id)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest();
            }
            else
            {
                return NoContent();
            }
        }

        [HttpDelete("/Products/{id}")]
#if AUTHORIZE
        [Authorize]
#endif
        public ActionResult<Product> DeleteProduct([FromRoute] int id)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest();
            }
            else
            {
                return NoContent();
            }
        }
    }

    public record Page(int PageNumber, int PageSize);

    public class Product
    {
        public Product(
            int id,
            string name,
            int supplierId,
            int categoryId,
            int quantityPerUnit,
            decimal unitPrice,
            int unitsInStock,
            int unitsOnOrder,
            int reorderLevel,
            bool discontinued)
        {
            Id = id;
            Name = name;
            SupplierId = supplierId;
            CategoryId = categoryId;
            QuantityPerUnit = quantityPerUnit;
            UnitPrice = unitPrice;
            UnitsInStock = unitsInStock;
            UnitsOnOrder = unitsOnOrder;
            ReorderLevel = reorderLevel;
            Discontinued = discontinued;
        }

        public int Id { get; set; }
        public string Name { get; set; }
        public int SupplierId { get; set; }
        public int CategoryId { get; set; }
        public int QuantityPerUnit { get; set; }
        public decimal UnitPrice { get; set; }
        public int UnitsInStock { get; set; }
        public int UnitsOnOrder { get; set; }
        public int ReorderLevel { get; set; }
        public bool Discontinued { get; set; }
    }
}
