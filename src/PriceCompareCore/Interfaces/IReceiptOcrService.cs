using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using PriceCompareData.Entities;

namespace PriceCompareCore.Services
{
    /**
    * Represents a line of text detected by OCR.
    */
    public class OcrTextLine
    {
        public string Text { get; set; }
        public float Confidence { get; set; }
    }

    public class ReceiptOcrResult
    {
        public List<OcrTextLine> Lines { get; set; }

        public ReceiptOcrResult()
        {
            Lines = new List<OcrTextLine>();
        }
    }

    public interface IReceiptOcrService
    {
        // imageKey: S3 object key of the receipt image
        Task<ReceiptOcrResult> AnalyzeAsync(string imageKey);
    }
}