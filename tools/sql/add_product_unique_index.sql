-- Find duplicates before adding the unique index.
SELECT shoptype, sourceid, COUNT(*) AS cnt
FROM product
WHERE sourceid IS NOT NULL
GROUP BY shoptype, sourceid
HAVING COUNT(*) > 1;

-- Add a partial unique index to support ON CONFLICT for non-null sourceid.
CREATE UNIQUE INDEX IF NOT EXISTS ux_product_shoptype_sourceid
    ON product (shoptype, sourceid)
    WHERE sourceid IS NOT NULL;
