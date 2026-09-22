-- BTS-151: seed job_definitions with the Quartz jobs registered in src/PriceCompareWeb/Program.cs.
--
-- Quartz no longer runs inside the API Lambda (Scraping__EnableQuartz=false), so the admin
-- schedules page lists Quartz jobs from this table instead of the live scheduler.
-- Keep this file in sync whenever a Quartz job or cron expression changes in Program.cs.
--
-- Check what is already there first:
--   SELECT job_name, schedule_expression, timezone, enabled
--   FROM job_definitions WHERE source = 'quartz' ORDER BY job_name;
--
-- Idempotent: re-running updates schedule/timezone/enabled and keeps any existing description.

INSERT INTO job_definitions (job_name, source, schedule_expression, timezone, enabled, description, created_at, updated_at)
VALUES
    ('ColesRefreshJob',                  'quartz', '0 5 0 ? * WED',       'Australia/Sydney', true,  'Coles Down Down deals',                        now(), now()),
    ('ColesMeatSeafoodDomJob',           'quartz', '0 15 0 ? * WED',      'Australia/Sydney', true,  'Coles - Meat & Seafood',                       now(), now()),
    ('ColesFruitVegetablesDomJob',       'quartz', '0 25 0 ? * WED',      'Australia/Sydney', true,  'Coles - Fruit & Vegetables',                   now(), now()),
    ('ColesDairyEggsFridgeDomJob',       'quartz', '0 35 0 ? * WED',      'Australia/Sydney', true,  'Coles - Dairy, Eggs & Fridge',                 now(), now()),
    ('ColesBakeryDomJob',                'quartz', '0 45 0 ? * WED',      'Australia/Sydney', true,  'Coles - Bakery',                               now(), now()),
    ('ColesDeliDomJob',                  'quartz', '0 55 0 ? * WED',      'Australia/Sydney', true,  'Coles - Deli',                                 now(), now()),
    ('ColesPantryDomJob',                'quartz', '0 5 1 ? * WED',       'Australia/Sydney', true,  'Coles - Pantry',                               now(), now()),
    ('ColesDietaryWorldFoodsDomJob',     'quartz', '0 15 1 ? * WED',      'Australia/Sydney', true,  'Coles - Dietary & World Foods',                now(), now()),
    ('ColesChipsChocolatesSnacksDomJob', 'quartz', '0 25 1 ? * WED',      'Australia/Sydney', true,  'Coles - Chips, Chocolates & Snacks',           now(), now()),
    ('ColesDrinksDomJob',                'quartz', '0 35 1 ? * WED',      'Australia/Sydney', true,  'Coles - Drinks',                               now(), now()),
    ('ColesLiquorlandDomJob',            'quartz', '0 45 1 ? * WED',      'Australia/Sydney', true,  'Coles - Liquorland',                           now(), now()),
    ('ColesFrozenDomJob',                'quartz', '0 55 1 ? * WED',      'Australia/Sydney', true,  'Coles - Frozen',                               now(), now()),
    ('ColesCleaningLaundryDomJob',       'quartz', '0 5 2 ? * WED',       'Australia/Sydney', true,  'Coles - Cleaning & Laundry',                   now(), now()),
    ('ColesHealthBeautyDomJob',          'quartz', '0 15 2 ? * WED',      'Australia/Sydney', true,  'Coles - Health & Beauty',                      now(), now()),
    ('ColesBabyDomJob',                  'quartz', '0 25 2 ? * WED',      'Australia/Sydney', true,  'Coles - Baby',                                 now(), now()),
    ('ColesPetDomJob',                   'quartz', '0 35 2 ? * WED',      'Australia/Sydney', true,  'Coles - Pet',                                  now(), now()),
    ('ColesHomeGardenDomJob',            'quartz', '0 45 2 ? * WED',      'Australia/Sydney', true,  'Coles - Home & Garden',                        now(), now()),
    ('ColesBigPackValueDomJob',          'quartz', '0 55 2 ? * WED',      'Australia/Sydney', true,  'Coles - Big Pack Value',                       now(), now()),
    ('ColesBonusCreditProductsDomJob',   'quartz', '0 5 3 ? * WED',       'Australia/Sydney', true,  'Coles - Bonus Credit Products',                now(), now()),
    ('ColesDeliverMoreRangeDomJob',      'quartz', '0 15 3 ? * WED',      'Australia/Sydney', true,  'Coles - Deliver More Range',                   now(), now()),
    ('WwsLowerShelfDomJob',              'quartz', '0 45 3 ? * WED',      'Australia/Sydney', true,  'Woolworths - Lower Shelf price',               now(), now()),
    ('WwsEverydayLowPriceDomJob',        'quartz', '0 15 4 ? * WED',      'Australia/Sydney', true,  'Woolworths - Everyday Low Price',              now(), now()),
    ('WwsHalfPriceDomJob',               'quartz', '0 45 4 ? * WED',      'Australia/Sydney', true,  'Woolworths - Half Price',                      now(), now()),
    ('WwsBuyMoreSaveMoreDomJob',         'quartz', '0 45 5 ? * WED',      'Australia/Sydney', true,  'Woolworths - Buy More Save More',              now(), now()),
    ('WwsSummerPriceDomJob',             'quartz', '',                    'Australia/Sydney', false, 'Woolworths - Summer Price (trigger disabled, replaced by Autumn Price)', now(), now()),
    ('WwsAutumnPriceDomJob',             'quartz', '0 15 6 ? * WED',      'Australia/Sydney', true,  'Woolworths - Autumn Price',                    now(), now()),
    ('CleanPriceHistoryJob',             'quartz', '0 0 6 ? 1/3 TUE#1',   'Australia/Sydney', true,  'Quarterly price history cleanup',              now(), now()),
    ('FavoritePriceTrackingJob',         'quartz', '0 55 6 ? * WED',      'Australia/Sydney', true,  'Favourite price-drop digest (local copy; prod runs the AWS Lambda)', now(), now())
ON CONFLICT (job_name, source) DO UPDATE
SET schedule_expression = EXCLUDED.schedule_expression,
    timezone            = EXCLUDED.timezone,
    enabled             = EXCLUDED.enabled,
    description         = COALESCE(job_definitions.description, EXCLUDED.description),
    updated_at          = now();
