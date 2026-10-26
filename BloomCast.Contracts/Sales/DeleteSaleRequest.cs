using System.ComponentModel.DataAnnotations;

namespace BloomCast.Contracts.Sales;

public class DeleteSaleRequest
{
    [Required]
    public string Password { get; set; } = "";
}