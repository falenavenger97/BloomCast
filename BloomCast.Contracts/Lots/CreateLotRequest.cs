using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.ComponentModel.DataAnnotations;

namespace BloomCast.Contracts.Lots;

public class CreateLotRequest
{
    [Required]
    [Range(1, int.MaxValue)]
    public int OrquideaId { get; set; }

    [Required]
    [Range(1, 9999)]
    public int CantidadActual { get; set; }
}
