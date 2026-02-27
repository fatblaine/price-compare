namespace PriceCompareData.DTOs
{
    public class MatchLlmReviewRequest
    {
        public int SourceShop { get; set; }
        public int TargetShop { get; set; }
        public int Limit { get; set; } = 1000;
        public int TopN { get; set; } = 20;
        public bool Force { get; set; } = false;
    }
}
