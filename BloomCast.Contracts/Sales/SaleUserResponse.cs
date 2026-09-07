using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BloomCast.Contracts.Sales;

public class SaleUserResponse
{
    public int Id { get; set; }

    public string NombreCompleto { get; set; } = "";
}
