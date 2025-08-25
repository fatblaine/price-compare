using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace PriceCompareData.Common
{
    public class Xpath
    {
        public const string DOWN_DOWN_PRODUCTS_NODE = "//*[@id='coles-targeting-product-tiles']//section[@data-testid='product-tile']";
        public const string DOWN_DOWN_PRODUCTS_NAME = ".//a[contains(@class, 'product__link')]/h2[contains(@class, 'product__title')]";
        public const string DOWN_DOWN_PRODUCTS_PRICE = ".//span[contains(@class, 'price')]";
        public const string DOWN_DOWN_PRODUCTS_PRICE_PER_UNIT = ".//div[contains(@class, 'price__calculation_method')]";
        public const string DOWN_DOWN_PRODUCTS_ORIGINAL_PRICE = ".//div[contains(@class, 'price__was')]/strong";
        public const string DOWN_DOWN_PRODUCTS_IMAGE_URL = ".//img[@data-testid='product-image']";
        public const string DOWN_DOWN_PRODUCTS_IS_SPONSORED = ".//li[contains(@class, 'product__top_messaging__item') and contains(text(), 'Sponsored')]";
    }
}