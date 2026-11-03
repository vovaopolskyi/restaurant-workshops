using System.ComponentModel.DataAnnotations;

namespace APIHandler.Models.GroupReservation;

public class SubmitGroupPreOrderRequest
{
    [Required]
    public string ReservationId { get; init; } = string.Empty;
}
