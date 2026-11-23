-- BTS-167 首页比价推荐慢:POST /api/compare-cached/by-products 的反向查找(WHERE targetproductid = ANY(@missing))
-- 没有可用索引,只能全表扫描 productmatch(2026-11-23 线上 27.6 万行 / 146 MB)。
-- 表在缓存里时约 30 ms,被挤出缓存后 2–4 s(CloudWatch 7 天:p90 2.2 s,最慢 4.2 s,76 次里 11 次超过 1 s)。
--
-- 现有索引都以 sourceproductid 开头,反向查找用不上:
--   productmatch_pkey (id)
--   uq_productmatch_pair (sourceproductid, targetproductid)
--   idx_productmatch_source_score (sourceproductid, ...)
--
-- 幂等:可重复执行。先在 dev 执行,确认无误后再在 prod 执行。
-- CONCURRENTLY 不锁表,线上读写不受影响;但不能放在事务里 ——
-- 在 Supabase SQL Editor 里请单独选中下面这一句执行,不要和其他语句一起运行。

CREATE INDEX CONCURRENTLY IF NOT EXISTS idx_productmatch_target
    ON productmatch (targetproductid);

-- 检查(可选):
-- 1) 索引存在且有效(indisvalid = true;如果建到一半中断会是 false,需 DROP INDEX 后重建)
-- SELECT c.relname, i.indisvalid
-- FROM pg_index i JOIN pg_class c ON c.oid = i.indexrelid
-- WHERE c.relname = 'idx_productmatch_target';
--
-- 2) 反向查找走新索引(应看到 Index Scan / Bitmap Index Scan using idx_productmatch_target,而不是 Seq Scan)
-- EXPLAIN ANALYZE
-- SELECT * FROM productmatch
-- WHERE targetproductid = ANY (ARRAY(SELECT productid FROM product LIMIT 20));
