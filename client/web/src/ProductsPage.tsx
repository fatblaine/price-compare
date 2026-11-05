import * as React from "react";
import { useState, useEffect } from "react";
import Box from "@mui/material/Box";
import {
	Button,
	Stack,
	FormControl,
	InputLabel,
	Select,
	MenuItem,
	Typography,
	Card,
	Pagination,
	LinearProgress,
	IconButton,
	InputBase,
} from "@mui/material";
import FavoriteIcon from "@mui/icons-material/Favorite";
import FavoriteBorderIcon from "@mui/icons-material/FavoriteBorder";
import SearchIcon from "@mui/icons-material/Search";
import ArrowDownwardIcon from "@mui/icons-material/ArrowDownward";
import { useMediaQuery, useTheme } from "@mui/material";
import { SHOP_OPTIONS, shopColors, shopTypeName } from "./constants/shopTypes";
import {
	fetchLastUpdated,
	fetchProducts,
	formatUpdatedDate,
	isStale,
	type LastUpdated,
	type ProductRow,
} from "./api/products";
import { useDebounce } from "./hooks/useDebounce";
import CompareDialog from "./components/CompareDialog";
import StoreChip from "./components/StoreChip";
import { fetchBestMatchesByProducts, type CompareProduct } from "./api/compare";
import { addFavorite, fetchFavorites, removeFavorite } from "./api/favorites";
import { useAuth } from "react-oidc-context";
import { useTour } from "./hooks/useTour";
import TourButton from "./components/TourButton";
import AutoAwesomeIcon from "@mui/icons-material/AutoAwesome";
import { Tooltip } from "@mui/material";
import SearchByDescriptionDialog from "./components/SearchByDescriptionDialog";
import { DISPLAY_FONT, tokens } from "./theme";

function formatSize(row: ProductRow) {
	if (row.size && row.size.trim() !== "") return row.size;
	const v = row.sizeValue;
	const u = row.sizeUnit;
	if (v != null && u) return `${v} ${u}`;
	if (v != null) return String(v);
	if (u) return u;
	return "";
}

function formatPrice(price?: number) {
	if (price == null || !Number.isFinite(price)) return "-";
	return `$${price.toFixed(2)}`;
}

/** "Updated 21 Oct · 2 stores": green while fresh, amber once older than a week. */
function FreshnessChip({ data }: { data: LastUpdated | null }) {
	const label = formatUpdatedDate(data?.lastUpdatedAt);
	if (!data || !label) return null;
	const stale = isStale(data.lastUpdatedAt);
	const shops = Object.entries(data.shops);
	const storeCount = shops.filter(([, iso]) => iso).length;
	const perShop = shops
		.map(([type, iso]) => `${shopTypeName(Number(type))}: ${formatUpdatedDate(iso) ?? "no data"}`)
		.join(" · ");
	return (
		<Box
			component="span"
			title={perShop}
			sx={{
				display: "inline-flex",
				alignItems: "center",
				gap: 0.75,
				height: 26,
				px: 1.25,
				borderRadius: 999,
				bgcolor: stale ? tokens.promoTint : tokens.saveTint,
				color: stale ? tokens.promo : tokens.save,
				fontSize: 12,
				fontWeight: 600,
				whiteSpace: "nowrap",
			}}
		>
			<Box
				component="span"
				aria-hidden="true"
				sx={{ width: 7, height: 7, borderRadius: "50%", bgcolor: "currentColor" }}
			/>
			Updated {label} · {storeCount} {storeCount === 1 ? "store" : "stores"}
		</Box>
	);
}

function ProductImage({
	src,
	alt,
}: {
	src?: string | null;
	alt: string;
}) {
	const [failed, setFailed] = React.useState(false);
	if (!src || failed) {
		const initial =
			alt && alt.trim() !== "" ? alt.trim().charAt(0).toUpperCase() : "?";
		return (
			<Box
				aria-hidden="true"
				sx={{ color: tokens.ink3, fontSize: { xs: 32, sm: 44 }, fontWeight: 700, fontFamily: DISPLAY_FONT }}
			>
				{initial}
			</Box>
		);
	}

	return (
		<Box
			component="img"
			src={src}
			alt={alt}
			loading="lazy"
			onError={() => setFailed(true)}
			sx={{
				height: "100%",
				width: "100%",
				objectFit: "contain",
				// Product shots sit on white; multiply lets them blend into the image ground.
				mixBlendMode: "multiply",
			}}
		/>
	);
}

export default function ProductsPage() {
	const theme = useTheme();
	const isXs = useMediaQuery(theme.breakpoints.down("sm"));
	const isMdDown = useMediaQuery(theme.breakpoints.down("md"));
	const { isAuthenticated } = useAuth();
	const [rows, setRows] = useState<ProductRow[]>([]);
	const [rowCount, setRowCount] = useState(0);
	const [loading, setLoading] = useState(false);
	const [search, setSearch] = useState("");
	const [shopType, setShopType] = useState("");
	const [paginationModel, setPaginationModel] = useState({
		page: 0,
		pageSize: 20,
	});
	const [refreshTick, setRefreshTick] = useState(0);
	const [favoriteIds, setFavoriteIds] = useState<Set<string>>(new Set());
	const [favoriteBusyIds, setFavoriteBusyIds] = useState<string[]>([]);
	const [compareDataMap, setCompareDataMap] = useState<Record<string, CompareProduct | null>>({});

	// Compare dialog state
	const [compareOpen, setCompareOpen] = useState(false);
	const [compareKeyword, setCompareKeyword] = useState<string>("");
	const [compareSourceShop, setCompareSourceShop] = useState<
		number | undefined
	>(undefined);
	const [compareSourceProductId, setCompareSourceProductId] = useState<
		string | undefined
	>(undefined);
	const [compareSourceProduct, setCompareSourceProduct] = useState<
		CompareProduct | undefined
	>(undefined);

	// AI search dialog state
	const [aiSearchOpen, setAiSearchOpen] = useState(false);
	const [aiSearchExhausted, setAiSearchExhausted] = useState(false);

	// Data freshness for the header chip and the compare dialog (BTS-161).
	const [lastUpdated, setLastUpdated] = useState<LastUpdated | null>(null);
	useEffect(() => {
		let active = true;
		fetchLastUpdated()
			.then((data) => {
				if (active) setLastUpdated(data);
			})
			.catch((e) => {
				// eslint-disable-next-line no-console
				console.error("Failed to load last-updated", e);
			});
		return () => {
			active = false;
		};
	}, []);

	const debouncedName = useDebounce(search, 350);
	const { startTour, autoStart } = useTour();

	const handleCompare = (
		productId: string,
		name?: string,
		shopTypeValue?: number | string | null,
		row?: ProductRow,
	) => {
		if (!name) {
			alert("No product name available for comparison.");
			return;
		}
		setCompareKeyword(name);
		setCompareSourceProductId(productId);
		const source = Number(shopTypeValue ?? NaN);
		setCompareSourceShop(
			Number.isFinite(source) ? (source as number) : undefined,
		);
		if (row) {
			const shopTypeNum = Number(row.shopType ?? NaN);
			const sizeValue = row.sizeValue ?? null;
			const price = row.price ?? null;
			setCompareSourceProduct({
				productId: row.productId,
				name: row.name,
				shopType: Number.isFinite(shopTypeNum) ? shopTypeNum : NaN,
				brand: undefined,
				sizeValue,
				sizeUnit: row.sizeUnit ?? null,
				size: row.size ?? null,
				price,
				pricePerUnit:
					price != null && sizeValue != null && sizeValue > 0
						? price / sizeValue
						: null,
			});
		} else {
			setCompareSourceProduct(undefined);
		}
		setCompareOpen(true);
	};

	const setFavoriteBusy = React.useCallback((productId: string, busy: boolean) => {
		setFavoriteBusyIds((prev) => {
			if (busy) {
				return prev.includes(productId) ? prev : [...prev, productId];
			}
			return prev.filter((item) => item !== productId);
		});
	}, []);

	const handleToggleFavorite = React.useCallback(
		async (productId: string) => {
			if (!isAuthenticated) {
				alert("Please sign in to add items to My Favorites.");
				return;
			}
			if (favoriteBusyIds.includes(productId)) return;
			const wasFavorite = favoriteIds.has(productId);
			setFavoriteBusy(productId, true);
			setFavoriteIds((prev) => {
				const next = new Set(prev);
				if (wasFavorite) {
					next.delete(productId);
				} else {
					next.add(productId);
				}
				return next;
			});
			try {
				if (wasFavorite) {
					await removeFavorite(productId);
				} else {
					await addFavorite(productId);
				}
			} catch (e) {
				setFavoriteIds((prev) => {
					const next = new Set(prev);
					if (wasFavorite) {
						next.add(productId);
					} else {
						next.delete(productId);
					}
					return next;
				});
				// eslint-disable-next-line no-console
				console.error("Failed to toggle favorite", e);
			} finally {
				setFavoriteBusy(productId, false);
			}
		},
		[
			favoriteBusyIds,
			favoriteIds,
			isAuthenticated,
			setFavoriteBusy,
		],
	);

	const hasAutoStarted = React.useRef(false);
	useEffect(() => {
		if (!hasAutoStarted.current && rows.length > 0) {
			hasAutoStarted.current = true;
			autoStart();
		}
	}, [rows, autoStart]);

	// Fetch when filters or pagination changes
	useEffect(() => {
		const doFetch = async () => {
			try {
				setLoading(true);
				const page = paginationModel.page + 1; // backend is 1-based
				const pageSize = paginationModel.pageSize;
				const shopTypeNumber =
					shopType.trim() === "" ? undefined : Number(shopType);
				const { items, total } = await fetchProducts({
					page,
					pageSize,
					name: debouncedName || undefined,
					shopType: Number.isFinite(shopTypeNumber)
						? (shopTypeNumber as number)
						: undefined,
				});
				setRows(items);
				setRowCount(total);
			} catch (e) {
				console.error(e);
			} finally {
				setLoading(false);
			}
		};
		doFetch();
	}, [
		debouncedName,
		shopType,
		paginationModel.page,
		paginationModel.pageSize,
		refreshTick,
	]);

	useEffect(() => {
		let active = true;
		if (!isAuthenticated) {
			setFavoriteIds(new Set());
			return () => {
				active = false;
			};
		}
		const loadFavorites = async () => {
			try {
				const data = await fetchFavorites();
				if (!active) return;
				setFavoriteIds(new Set(data.map((item) => item.productId)));
			} catch (e) {
				// eslint-disable-next-line no-console
				console.error("Failed to load favorites", e);
			}
		};
		void loadFavorites();
		return () => {
			active = false;
		};
	}, [isAuthenticated]);

	useEffect(() => {
		if (rows.length === 0) {
			setCompareDataMap({});
			return;
		}
		setCompareDataMap({});
		let cancelled = false;

		// BTS-153: one batch request for the whole page. Previously this fired one
		// request per product (3 at a time), which also forced extra Lambda cold starts.
		const loadBestMatches = async () => {
			const ids = rows.map((row) => row.productId);
			try {
				const best = await fetchBestMatchesByProducts(ids);
				if (cancelled) return;
				setCompareDataMap(best);
			} catch {
				if (cancelled) return;
				setCompareDataMap(
					Object.fromEntries(ids.map((id) => [id, null])),
				);
			}
		};

		void loadBestMatches();

		return () => {
			cancelled = true;
		};
	}, [rows]);

	const totalPages = Math.max(
		1,
		Math.ceil(rowCount / Math.max(1, paginationModel.pageSize)),
	);

	const resetToFirstPage = () =>
		setPaginationModel((prev) => (prev.page === 0 ? prev : { ...prev, page: 0 }));

	return (
		<Box sx={{ width: "100%" }}>
			{/* Page header: freshness, headline, search and store filter. */}
			<Box
				component="section"
				sx={{
					bgcolor: tokens.surface,
					// Phones: a full-width white band under the app bar; larger screens: a card.
					border: { xs: "none", sm: `1px solid ${tokens.line}` },
					borderBottom: `1px solid ${tokens.line}`,
					borderRadius: { xs: 0, sm: "18px" },
					boxShadow: { xs: "none", sm: "0 1px 2px rgba(20,22,30,.04)" },
					mx: { xs: -2, sm: 0 },
					mt: { xs: -2, md: 0 },
					px: { xs: 2, sm: 3, md: 4 },
					pt: { xs: 1.75, sm: 3.5, md: 4.5 },
					pb: { xs: 1.75, sm: 3 },
					mb: { xs: 2, sm: 3 },
				}}
			>
				<FreshnessChip data={lastUpdated} />
				<Box sx={{ display: { xs: "none", sm: "block" }, maxWidth: 640 }}>
					<Typography
						component="h2"
						sx={{
							fontFamily: DISPLAY_FONT,
							fontWeight: 800,
							letterSpacing: "-0.03em",
							fontSize: { sm: 32, md: 40 },
							lineHeight: 1.08,
							mt: 2,
							mb: 1,
						}}
					>
						Compare Coles &amp; Woolworths.
						<br />
						Buy the cheaper one.
					</Typography>
					<Typography sx={{ color: tokens.ink2, fontSize: { sm: 16, md: 17 }, lineHeight: 1.5 }}>
						Grocery prices from both stores, matched product by product, with
						price history so you can see where it&apos;s cheaper.
					</Typography>
				</Box>

				<Box
					sx={{
						mt: { xs: 1.5, sm: 3 },
						display: "flex",
						alignItems: "center",
						gap: { xs: 1, sm: 1.25 },
						p: { xs: 0, sm: 1.25 },
						bgcolor: tokens.surface,
						border: { xs: "none", sm: `1px solid ${tokens.line}` },
						borderRadius: "16px",
						boxShadow: {
							xs: "none",
							sm: "0 1px 2px rgba(20,22,30,.04), 0 8px 22px rgba(20,22,30,.06)",
						},
					}}
				>
					<Box
						id="search-input"
						sx={{
							flex: 1,
							minWidth: 0,
							display: "flex",
							alignItems: "center",
							gap: 1.25,
							height: 46,
							px: 1.5,
							borderRadius: "12px",
							bgcolor: { xs: "#F7F8FA", sm: "transparent" },
							border: { xs: `1px solid ${tokens.line}`, sm: "none" },
						}}
					>
						<SearchIcon aria-hidden="true" sx={{ color: tokens.ink3, fontSize: 20 }} />
						<InputBase
							placeholder={isXs ? "Search products…" : "Search products: milk, Tim Tams, olive oil…"}
							inputProps={{ "aria-label": "Search products" }}
							value={search}
							onChange={(e) => {
								setSearch(e.target.value);
								resetToFirstPage();
							}}
							onKeyDown={(e) => {
								if (e.key === "Enter") setRefreshTick((v) => v + 1);
							}}
							sx={{ flex: 1, minWidth: 0, fontSize: { xs: 14, sm: 16 }, fontWeight: 500 }}
						/>
					</Box>
					<Tooltip
						title={
							aiSearchExhausted
								? "Daily AI search limit reached. Try again tomorrow."
								: "Describe what you're looking for (AI search, any language)"
						}
					>
						<span>
							<Button
								id="ai-search-btn"
								variant="outlined"
								onClick={() => setAiSearchOpen(true)}
								disabled={aiSearchExhausted}
								aria-label="Search by description with AI"
								sx={{
									height: { xs: 46, sm: 44 },
									minWidth: { xs: 46, sm: 0 },
									px: { xs: 0, sm: 2 },
									borderColor: aiSearchOpen ? tokens.brand : undefined,
								}}
							>
								<AutoAwesomeIcon aria-hidden="true" sx={{ color: tokens.brand, fontSize: 18 }} />
								<Box component="span" sx={{ display: { xs: "none", sm: "inline" }, ml: 1 }}>
									AI search
								</Box>
							</Button>
						</span>
					</Tooltip>
					<Button
						variant="contained"
						sx={{ height: 44, display: { xs: "none", sm: "inline-flex" } }}
						onClick={() => setRefreshTick((v) => v + 1)}
					>
						Search
					</Button>
				</Box>

				<Box
					sx={{
						mt: { xs: 1.5, sm: 2.25 },
						display: "flex",
						alignItems: "center",
						gap: 1,
						// Phones: the row scrolls sideways instead of wrapping.
						overflowX: "auto",
						mx: { xs: -2, sm: 0 },
						px: { xs: 2, sm: 0 },
						scrollbarWidth: "none",
						"&::-webkit-scrollbar": { display: "none" },
					}}
				>
					<Box
						id="shop-filter"
						role="group"
						aria-label="Filter by store"
						sx={{ display: "flex", gap: 1, flexShrink: 0 }}
					>
						{SHOP_OPTIONS.map((opt) => {
							const selected = shopType === opt.value;
							const dot = opt.value === "" ? null : shopColors(opt.value).main;
							return (
								<Button
									key={String(opt.value)}
									aria-pressed={selected}
									onClick={() => {
										setShopType(opt.value);
										resetToFirstPage();
										// Force refresh immediately after changing shop type
										setRefreshTick((v) => v + 1);
									}}
									sx={{
										height: 40,
										px: 1.75,
										borderRadius: 999,
										flexShrink: 0,
										whiteSpace: "nowrap",
										fontSize: { xs: 13, sm: 14 },
										border: "1px solid",
										borderColor: selected ? tokens.ink : tokens.line,
										bgcolor: selected ? tokens.ink : tokens.surface,
										color: selected ? "#ffffff" : tokens.ink2,
										"&:hover": {
											bgcolor: selected ? tokens.ink : tokens.surface,
											borderColor: selected ? tokens.ink : tokens.lineStrong,
										},
									}}
								>
									{dot && (
										<Box
											component="span"
											aria-hidden="true"
											sx={{ width: 8, height: 8, borderRadius: "50%", bgcolor: dot, mr: 0.875 }}
										/>
									)}
									{opt.value === "" ? "All stores" : opt.label}
								</Button>
							);
						})}
					</Box>
					<Box sx={{ flex: 1 }} />
					<Box sx={{ flexShrink: 0 }}>
						<TourButton onClick={startTour} disabled={compareOpen} />
					</Box>
				</Box>
			</Box>

			<Typography sx={{ fontSize: { xs: 13, sm: 14 }, color: tokens.ink2, mb: { xs: 1.5, sm: 2 } }}>
				<Box component="strong" sx={{ color: tokens.ink, fontWeight: 700, fontVariantNumeric: "tabular-nums" }}>
					{rowCount.toLocaleString("en-AU")}
				</Box>{" "}
				products
			</Typography>

			<Box sx={{ position: "relative" }}>
				{loading && (
					<LinearProgress
						sx={{ position: "absolute", left: 0, right: 0, top: -10, borderRadius: 1 }}
					/>
				)}
				{rows.length === 0 && !loading ? (
					<Box
						sx={{
							py: 8,
							textAlign: "center",
							color: "text.secondary",
						}}
					>
						<Typography variant="body1" sx={{ fontWeight: 600 }}>
							No products found
						</Typography>
						<Typography variant="body2">
							Try adjusting your filters.
						</Typography>
					</Box>
				) : (
					<Box
						sx={{
							display: "grid",
							gridTemplateColumns: {
								xs: "repeat(2, minmax(0, 1fr))",
								sm: "repeat(auto-fill, minmax(224px, 1fr))",
							},
							gap: { xs: 1.5, sm: 2.25 },
						}}
					>
						{rows.map((row) => {
							const sizeText = formatSize(row);
							const isFavorite = favoriteIds.has(row.productId);
							const favoriteBusy = favoriteBusyIds.includes(row.productId);
							const compareTarget = compareDataMap[row.productId];
							// Same comparison as before; only the wording and colour changed (BTS-161).
							let recommendation: { text: string; save: boolean } | null = null;
							if (compareTarget && row.price != null && compareTarget.price != null) {
								const diff = compareTarget.price - row.price;
								const other = shopTypeName(compareTarget.shopType);
								if (Math.abs(diff) < 0.001) {
									recommendation = { text: `Same price at ${other}`, save: false };
								} else if (diff < 0) {
									recommendation = {
										text: `${other} is $${Math.abs(diff).toFixed(2)} cheaper`,
										save: false,
									};
								} else {
									recommendation = { text: `Save $${diff.toFixed(2)} vs ${other}`, save: true };
								}
							}
							return (
								<Card
									key={row.productId}
									className="product-card"
									sx={{
										display: "flex",
										flexDirection: "column",
										overflow: "hidden",
										borderRadius: { xs: "14px", sm: "16px" },
										transition: "box-shadow .2s ease, border-color .2s ease",
										"&:hover": {
											borderColor: tokens.lineStrong,
											boxShadow: "0 1px 2px rgba(20,22,30,.06), 0 16px 32px rgba(20,22,30,.08)",
										},
									}}
								>
									<Box
										sx={{
											position: "relative",
											height: { xs: 96, sm: 150 },
											bgcolor: tokens.imageGround,
											borderBottom: `1px solid ${tokens.line}`,
											display: "flex",
											alignItems: "center",
											justifyContent: "center",
											p: { xs: 1, sm: 1.5 },
										}}
									>
										<ProductImage src={row.imageUrl} alt={row.name} />
										<IconButton
											className="favorite-btn"
											onClick={() => void handleToggleFavorite(row.productId)}
											disabled={favoriteBusy}
											aria-label={
												isFavorite
													? "Remove from My Favorites"
													: "Add to My Favorites"
											}
											aria-pressed={isFavorite}
											sx={{
												position: "absolute",
												top: { xs: 6, sm: 12 },
												right: { xs: 6, sm: 12 },
												width: 40,
												height: 40,
												bgcolor: tokens.surface,
												border: `1px solid ${tokens.line}`,
												boxShadow: "0 2px 8px rgba(20,22,30,.1)",
												color: isFavorite ? tokens.fav : tokens.ink3,
												"&:hover": { bgcolor: tokens.surface, color: tokens.fav },
											}}
										>
											{isFavorite ? (
												<FavoriteIcon sx={{ fontSize: 19 }} />
											) : (
												<FavoriteBorderIcon sx={{ fontSize: 19 }} />
											)}
										</IconButton>
									</Box>
									<Box
										sx={{
											p: { xs: "10px 11px 12px", sm: "14px 15px 16px" },
											display: "flex",
											flexDirection: "column",
											gap: { xs: 0.75, sm: 1.125 },
											flex: 1,
											minWidth: 0,
										}}
									>
										<Box sx={{ display: "flex", alignItems: "center", gap: 1, flexWrap: "wrap", minWidth: 0 }}>
											<StoreChip shopType={row.shopType} size="small" />
											{sizeText && (
												<Typography component="span" sx={{ fontSize: { xs: 11, sm: 12 }, color: tokens.ink3 }}>
													{sizeText}
												</Typography>
											)}
										</Box>
										<Typography
											component="h3"
											sx={{
												m: 0,
												fontSize: { xs: 13, sm: 15 },
												lineHeight: 1.35,
												fontWeight: 600,
												display: "-webkit-box",
												WebkitLineClamp: 2,
												WebkitBoxOrient: "vertical",
												overflow: "hidden",
												minHeight: { xs: 35, sm: 40 },
											}}
										>
											{row.name || "-"}
										</Typography>
										<Typography
											component="p"
											sx={{
												m: 0,
												fontFamily: DISPLAY_FONT,
												fontWeight: 800,
												letterSpacing: "-0.02em",
												fontSize: { xs: 20, sm: 26 },
												lineHeight: 1.1,
												fontVariantNumeric: "tabular-nums",
											}}
										>
											{formatPrice(row.price)}
										</Typography>
										{row.promoText && (
											<Box
												component="span"
												title={row.promoText}
												sx={{
													alignSelf: "flex-start",
													maxWidth: "100%",
													px: 1,
													py: 0.375,
													borderRadius: 999,
													bgcolor: tokens.promoTint,
													color: tokens.promo,
													fontSize: { xs: 10.5, sm: 11.5 },
													fontWeight: 600,
													overflow: "hidden",
													textOverflow: "ellipsis",
													whiteSpace: "nowrap",
												}}
											>
												{row.promoText}
											</Box>
										)}
										<Box sx={{ minHeight: 20, display: "flex", alignItems: "center" }}>
											{recommendation && (
												<Typography
													component="span"
													className="recommendation-text"
													sx={{
														display: "inline-flex",
														alignItems: "center",
														gap: 0.5,
														fontSize: { xs: 11.5, sm: 13 },
														fontWeight: recommendation.save ? 700 : 600,
														color: recommendation.save ? tokens.save : tokens.ink2,
														fontVariantNumeric: "tabular-nums",
													}}
												>
													{recommendation.save && (
														<ArrowDownwardIcon aria-hidden="true" sx={{ fontSize: 14 }} />
													)}
													{recommendation.text}
												</Typography>
											)}
										</Box>
										<Button
											className="compare-btn"
											variant="contained"
											fullWidth
											sx={{ mt: "auto", height: { xs: 40, sm: 44 } }}
											onClick={() =>
												handleCompare(
													row.productId,
													row.name,
													row.shopType,
													row,
												)
											}
										>
											{isXs ? "Compare" : "Compare prices"}
										</Button>
									</Box>
								</Card>
							);
						})}
					</Box>
				)}
			</Box>

			<Stack
				direction={{ xs: "column", sm: "row" }}
				spacing={2}
				sx={{ mt: 3 }}
				alignItems={{ xs: "stretch", sm: "center" }}
				justifyContent="flex-end"
			>
				<Pagination
					count={totalPages}
					page={paginationModel.page + 1}
					onChange={(_, page) =>
						setPaginationModel((prev) => ({
							...prev,
							page: page - 1,
						}))
					}
					color="primary"
					// Phones keep the 32px "medium" size so page numbers stay tappable.
					size={isMdDown && !isXs ? "small" : "medium"}
				/>
				<FormControl size="small" sx={{ minWidth: 120 }}>
					<InputLabel id="page-size-label">Page size</InputLabel>
					<Select
						labelId="page-size-label"
						label="Page size"
						value={String(paginationModel.pageSize)}
						onChange={(e) => {
							const nextSize = Number(e.target.value);
							setPaginationModel({ page: 0, pageSize: nextSize });
						}}
					>
						{[10, 20, 50].map((opt) => (
							<MenuItem key={opt} value={String(opt)}>
								{opt}
							</MenuItem>
						))}
					</Select>
				</FormControl>
			</Stack>

			<CompareDialog
				open={compareOpen}
				keyword={compareKeyword}
				sourceShop={compareSourceShop}
				sourceProductId={compareSourceProductId}
				sourceProduct={compareSourceProduct}
				lastUpdated={lastUpdated}
				onClose={() => setCompareOpen(false)}
			/>
			<SearchByDescriptionDialog
				open={aiSearchOpen}
				onClose={() => setAiSearchOpen(false)}
				onSelect={(productName) => {
					setSearch(productName);
					setAiSearchOpen(false);
				}}
				onQuotaUpdate={(remaining) => setAiSearchExhausted(remaining === 0)}
			/>
		</Box>
	);
}
