-- BTS-163 下周特价预告:建表 / 加列。
-- 幂等:可重复执行。先在 dev 执行,确认无误后再在 prod 执行。
-- 方案:docs/scraping/next-week-catalogue-plan.md §4.1
--
-- 对现有代码无影响:只加可空列 / 带默认值的列和新表,现有 EF 映射不读这些列。
-- 新表开启 RLS 但不加策略:应用用 postgres 连接(表的所有者,不受 RLS 限制),
-- 而 Supabase Data API(anon / authenticated)读不到这两张表。

BEGIN;

-- 1) 商品补 stockcode(WWS)。不进 sourceid:线上导入按 (shoptype, sourceid) 判断商品是否存在。
--    不加唯一约束:历史数据可能有重复商品,唯一约束会让导入失败。
ALTER TABLE product ADD COLUMN IF NOT EXISTS stockcode text;
CREATE INDEX IF NOT EXISTS ix_product_shoptype_stockcode
    ON product (shoptype, stockcode) WHERE stockcode IS NOT NULL;

-- 一次性回填:旧 API 抓取在 barcode 为空时把 stockcode 写进了 sourceid(1–9 位数字)。
-- barcode 是 12–14 位,不会被误认。
UPDATE product SET stockcode = sourceid
WHERE shoptype = 1 AND stockcode IS NULL AND sourceid ~ '^\d{1,9}$';

-- 2) catalogue 条目(每期每条一行)
CREATE TABLE IF NOT EXISTS catalogue_special (
    id             bigserial,
    shoptype       int           NOT NULL,          -- 0 Coles / 1 WWS
    saleid         int           NOT NULL,          -- SaleFinder 期号
    region         text          NOT NULL,          -- SaleFinder areaName,如 NSW Specialty / c-vic-met
    itemid         text          NOT NULL,          -- SaleFinder data-itemid
    name           text          NOT NULL,
    price          numeric(18,4) NOT NULL,
    priceunit      text,                            -- each | kg | pk | …
    wasprice       numeric(18,4),
    saveamount     numeric(18,4),
    unitpricetext  text,
    promotext      text,
    imageurl       text,
    validfrom      date          NOT NULL,          -- 悉尼日期
    validto        date          NOT NULL,
    scrapedat      timestamptz   NOT NULL,
    CONSTRAINT pk_catalogue_special PRIMARY KEY (id),
    CONSTRAINT ux_catalogue_special_item UNIQUE (shoptype, saleid, itemid)
);
CREATE INDEX IF NOT EXISTS ix_catalogue_special_validfrom
    ON catalogue_special (validfrom);
ALTER TABLE catalogue_special ENABLE ROW LEVEL SECURITY;

-- 3) 条目覆盖的商品(一条条目对应多个 SKU)。sku = WWS stockcode / Coles 商品 ID。
--    productid 由抓取时的匹配 SQL 在目标库里填写;不加外键(商品清理时不受牵连)。
CREATE TABLE IF NOT EXISTS catalogue_special_sku (
    shoptype   int   NOT NULL,
    saleid     int   NOT NULL,
    itemid     text  NOT NULL,
    sku        text  NOT NULL,
    productid  uuid,
    CONSTRAINT pk_catalogue_special_sku PRIMARY KEY (shoptype, saleid, itemid, sku)
);
CREATE INDEX IF NOT EXISTS ix_catalogue_special_sku_productid
    ON catalogue_special_sku (productid) WHERE productid IS NOT NULL;
ALTER TABLE catalogue_special_sku ENABLE ROW LEVEL SECURITY;

-- 4) 收藏预告(P4 用,先建好)
ALTER TABLE favoriteitem       ADD COLUMN IF NOT EXISTS notifyonpreview      boolean NOT NULL DEFAULT true;
ALTER TABLE favoriteitem       ADD COLUMN IF NOT EXISTS previewnotifiedfor   date;
ALTER TABLE favoriteitem       ADD COLUMN IF NOT EXISTS previewnotifiedprice numeric(18,4);
ALTER TABLE favoritepricealert ADD COLUMN IF NOT EXISTS kind                 text NOT NULL DEFAULT 'drop';

COMMIT;

-- ---------------------------------------------------------------------------
-- 执行后核对(结果贴回给我即可)
-- ---------------------------------------------------------------------------

-- a) product.productid 必须是 uuid,否则 catalogue_special_sku.productid 的类型要跟着改
SELECT column_name, data_type
FROM information_schema.columns
WHERE table_name = 'product' AND column_name IN ('productid', 'sourceid', 'stockcode');

-- b) 新表和新列都在
SELECT table_name, column_name, data_type, is_nullable, column_default
FROM information_schema.columns
WHERE (table_name IN ('catalogue_special', 'catalogue_special_sku'))
   OR (table_name = 'favoriteitem' AND column_name IN ('notifyonpreview', 'previewnotifiedfor', 'previewnotifiedprice'))
   OR (table_name = 'favoritepricealert' AND column_name = 'kind')
ORDER BY table_name, ordinal_position;

-- c) WWS 商品现在的编号情况(决定 stockcode 回填要等几轮抓取)
SELECT count(*)                                              AS wws_total,
       count(*) FILTER (WHERE sourceid IS NULL)               AS sourceid_null,
       count(*) FILTER (WHERE sourceid ~ '^\d{12,14}$')       AS sourceid_barcode,
       count(*) FILTER (WHERE sourceid ~ '^\d{1,9}$')         AS sourceid_short,
       count(stockcode)                                       AS stockcode_filled,
       count(*) FILTER (WHERE lastseenat > now() - interval '7 days') AS seen_last_7d
FROM product
WHERE shoptype = 1;
