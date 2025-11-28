using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace PriceCompareData.DTOs;

public record ReceiptDto(
    int Id,
    string StoreName,
    DateTime PurchaseDate,
    decimal TotalAmount,
    string? UploadUrl
);