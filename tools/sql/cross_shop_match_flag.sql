-- BTS-164 商品列表首页优先显示"两家都有"的商品:加列 / 加索引 / 回填。
-- 幂等:可重复执行。先在 dev 执行,确认无误后再在 prod 执行。
--
-- hascrossshopmatch = 该商品出现在任一条 matchtype = 'same_product' 的 productmatch 里(source 或 target)。
-- 之后由匹配任务、LLM 复核、手动改匹配自动刷新(CrossShopMatchFlags.cs,SQL 与本文件第 3 步相同)。
-- 如果 productmatch 是用别的方式(手动导入等)写进库的,导完再执行一次本文件即可。

BEGIN;

-- 1) 加列。带默认值,现有导入 SQL 显式列出列名,不受影响。
ALTER TABLE product ADD COLUMN IF NOT EXISTS hascrossshopmatch boolean NOT NULL DEFAULT false;

-- 2) 索引:对应 ORDER BY hascrossshopmatch DESC, productid。
CREATE INDEX IF NOT EXISTS ix_product_hascrossshopmatch_productid
    ON product (hascrossshopmatch DESC, productid);

-- 3) 回填(只改有变化的行)。
UPDATE product SET hascrossshopmatch = true
WHERE NOT hascrossshopmatch
  AND productid IN (
      SELECT sourceproductid FROM productmatch WHERE matchtype = 'same_product'
      UNION
      SELECT targetproductid FROM productmatch WHERE matchtype = 'same_product');

UPDATE product SET hascrossshopmatch = false
WHERE hascrossshopmatch
  AND productid NOT IN (
      SELECT sourceproductid FROM productmatch WHERE matchtype = 'same_product'
      UNION
      SELECT targetproductid FROM productmatch WHERE matchtype = 'same_product');

COMMIT;

ANALYZE product;

-- 4) 检查
-- SELECT shoptype, count(*) FILTER (WHERE hascrossshopmatch) AS flagged, count(*) AS total
-- FROM product GROUP BY shoptype;
--
-- 首页查询应走 ix_product_hascrossshopmatch_productid,且没有对全部当期商品的 Sort:
-- EXPLAIN (ANALYZE, BUFFERS)
-- SELECT p.* FROM product p
-- JOIN (SELECT shoptype, max(lastseenat) AS maxat FROM product WHERE shoptype IS NOT NULL GROUP BY shoptype) l
--   ON p.shoptype = l.shoptype
-- WHERE p.lastseenat >= date_trunc('day', l.maxat)
-- ORDER BY p.hascrossshopmatch DESC, p.productid
-- LIMIT 20 OFFSET 0;
