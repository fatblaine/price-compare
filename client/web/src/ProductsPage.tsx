import * as React from "react";
import { useState, useEffect } from "react";
import Box from "@mui/material/Box";
import {
	TextField,
	Button,
	Stack,
	FormControl,
	InputLabel,
	Select,
	MenuItem,
	Typography,
	Card,
	CardContent,
	Chip,
	Pagination,
	LinearProgress,
	IconButton,
} from "@mui/material";
import FavoriteIcon from "@mui/icons-material/Favorite";
import FavoriteBorderIcon from "@mui/icons-material/FavoriteBorder";
import { useMediaQuery, useTheme } from "@mui/material";
import type { SelectChangeEvent } from "@mui/material/Select";
import { SHOP_OPTIONS, shopTypeName } from "./constants/shopTypes";
import { fetchProducts, type ProductRow } from "./api/products";
import { useDebounce } from "./hooks/useDebounce";
import CompareDialog from "./components/CompareDialog";
import { fetchCompareMatchesByProduct, type CompareProduct } from "./api/compare";
import { addFavorite, fetchFavorites, removeFavorite } from "./api/favorites";
import { useAuth } from "react-oidc-context";
import { useTour } from "./hooks/useTour";
import TourButton from "./components/TourButton";
import AutoAwesomeIcon from "@mui/icons-material/AutoAwesome";
import { Tooltip } from "@mui/material";
import SearchByDescriptionDialog from "./components/SearchByDescriptionDialog";

function formatSize(row: ProductRow) {
	if (row.size && row.size.trim() !== "") return row.size;
	const v = row.sizeValue;
	const u = row.sizeUnit;
	if (v != null && u) return `${v} ${u}`;
	if (v != null) return String(v);
	if (u) return u;
	return "";
}

function formatPrice(price?: number, sizeText?: string) {
	if (price == null || !Number.isFinite(price)) return "-";
	const base = `$${price.toFixed(2)}`;
	if (sizeText) return `${base} / ${sizeText}`;
	return `${base} AUD`;
}

function shopAccent(shopType?: number | string | null) {
	if (shopType === 0 || shopType === "0") return "#c93925";
	if (shopType === 1 || shopType === "1") return "#3f8149";
	return "#4b5563";
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
				sx={{
					height: { xs: 130, sm: 170 },
					borderRadius: 4,
					bgcolor: "rgba(255,255,255,0.55)",
					display: "flex",
					alignItems: "center",
					justifyContent: "center",
				}}
			>
				<Box
					sx={{
						color: "#111111",
						fontSize: 48,
						fontWeight: 600,
					}}
				>
					{initial}
				</Box>
			</Box>
		);
	}

	return (
		<Box
			component="img"
			src={src}
			alt={alt}
			onError={() => setFailed(true)}
			sx={{
				height: 170,
				width: "100%",
				objectFit: "contain",
				borderRadius: 4,
				backgroundColor: "rgba(255,255,255,0.7)",
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

		const fetchWithConcurrency = async (items: typeof rows, limit: number) => {
			let i = 0;
			const worker = async () => {
				while (i < items.length) {
					const row = items[i++];
					if (cancelled) return;
					try {
						const targets = await fetchCompareMatchesByProduct(row.productId);
						if (cancelled) return;
						const best =
							targets.find(
								(t) => t.matchType === "same_product" && t.price != null,
							) ?? null;
						setCompareDataMap((prev) => ({ ...prev, [row.productId]: best }));
					} catch {
						if (cancelled) return;
						setCompareDataMap((prev) => ({ ...prev, [row.productId]: null }));
					}
				}
			};
			await Promise.all(Array.from({ length: limit }, worker));
		};

		void fetchWithConcurrency(rows, 3);

		return () => {
			cancelled = true;
		};
	}, [rows]);

	const totalPages = Math.max(
		1,
		Math.ceil(rowCount / Math.max(1, paginationModel.pageSize)),
	);

	return (
		<Box sx={{ width: "100%" }}>
			<Stack
				direction={{ xs: "column", sm: "row" }}
				spacing={2}
				alignItems="center"
				justifyContent="center"
				sx={{
					mb: 3,
					p: 1.5,
					borderRadius: 5,
					width: { xs: "100%", sm: "fit-content" },
					maxWidth: "100%",
					mx: "auto",
					background: "rgba(255,255,255,0.55)",
					backdropFilter: "blur(24px) saturate(180%)",
					WebkitBackdropFilter: "blur(24px) saturate(180%)",
					border: "0.5px solid rgba(255,255,255,0.7)",
					boxShadow: "0 8px 30px rgba(60,60,90,0.08)",
				}}
			>
				<TextField
					id="search-input"
					label="Search by name"
					variant="outlined"
					size="small"
					fullWidth={isXs}
					sx={{ width: { xs: "100%", sm: 240 } }}
					value={search}
					onChange={(e) => {
						setSearch(e.target.value);
						setPaginationModel((prev) =>
							prev.page === 0 ? prev : { ...prev, page: 0 },
						);
					}}
				/>
				<FormControl
					id="shop-filter"
					size="small"
					sx={{ minWidth: { xs: "100%", sm: 160 } }}
					fullWidth={isXs}
				>
					<InputLabel id="shop-type-label">Shop</InputLabel>
					<Select
						labelId="shop-type-label"
						label="Shop"
						value={shopType}
						onChange={(e: SelectChangeEvent) => {
							const value = e.target.value;
							setShopType(value);
							setPaginationModel((prev) =>
								prev.page === 0 ? prev : { ...prev, page: 0 },
							);
							// Force refresh immediately after changing shop type
							setRefreshTick((v) => v + 1);
						}}
					>
						{SHOP_OPTIONS.map((opt) => (
							<MenuItem key={String(opt.value)} value={opt.value}>
								{opt.label}
							</MenuItem>
						))}
					</Select>
				</FormControl>
				<Box
					sx={{
						display: "flex",
						alignItems: "center",
						justifyContent: "center",
						flexWrap: "wrap",
						gap: 1.5,
						width: { xs: "100%", sm: "auto" },
					}}
				>
				<Button
					variant="contained"
					sx={{ flexGrow: { xs: 1, sm: 0 } }}
					onClick={() => setRefreshTick((v) => v + 1)}
				>
					Search
				</Button>
				<Tooltip
				title={
					aiSearchExhausted
						? "Daily AI search limit reached. Try again tomorrow."
						: "Describe what you're looking for (AI search, any language)"
				}
			>
				<span>
					<IconButton
						id="ai-search-btn"
						onClick={() => setAiSearchOpen(true)}
						disabled={aiSearchExhausted}
						color={aiSearchOpen ? "primary" : "default"}
						size="small"
					>
						<AutoAwesomeIcon />
					</IconButton>
				</span>
			</Tooltip>
			<TourButton onClick={startTour} disabled={compareOpen} />
				</Box>
			</Stack>

			<Box
				sx={{
					position: "relative",
					borderRadius: 5,
					border: "0.5px solid rgba(255,255,255,0.6)",
					background: "rgba(255,255,255,0.35)",
					backdropFilter: "blur(20px) saturate(180%)",
					WebkitBackdropFilter: "blur(20px) saturate(180%)",
					boxShadow: "0 8px 30px rgba(60,60,90,0.06)",
					p: { xs: 1.5, sm: 2.5 },
					overflow: "hidden",
				}}
			>
				{loading && (
					<LinearProgress
						sx={{
							position: "absolute",
							left: 0,
							right: 0,
							top: 0,
						}}
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
								sm: "repeat(3, minmax(0, 1fr))",
								md: "repeat(4, minmax(0, 1fr))",
								lg: "repeat(5, minmax(0, 1fr))",
							},
							gap: { xs: 1, sm: 1.5, md: 2 },
						}}
					>
						{rows.map((row) => {
							const sizeText = formatSize(row);
							const priceText = formatPrice(row.price, sizeText || undefined);
							const accent = shopAccent(row.shopType);
							const isFavorite = favoriteIds.has(row.productId);
							const favoriteBusy = favoriteBusyIds.includes(row.productId);
							const compareTarget = compareDataMap[row.productId];
							let recommendationText: string | null = null;
							if (compareTarget && row.price != null && compareTarget.price != null) {
								const diff = compareTarget.price - row.price;
								if (Math.abs(diff) < 0.001) {
									recommendationText = "Both stores have the same price";
								} else if (diff < 0) {
									recommendationText = `${shopTypeName(compareTarget.shopType)} is cheaper! (save $${Math.abs(diff).toFixed(2)})`;
								} else {
									recommendationText = `${shopTypeName(row.shopType)} is cheaper! (save $${diff.toFixed(2)})`;
								}
							}
							return (
								<Card
									key={row.productId}
									className="product-card"
									elevation={0}
									sx={{
										display: "flex",
										flexDirection: "column",
										borderRadius: 6,
										border: "0.5px solid rgba(255,255,255,0.85)",
										background: "rgba(255,255,255,0.6)",
										backdropFilter: "blur(24px) saturate(180%)",
										WebkitBackdropFilter: "blur(24px) saturate(180%)",
										boxShadow: "0 10px 34px rgba(50,55,95,0.1)",
										transition: "transform .25s ease, box-shadow .25s ease",
										"&:hover": {
											transform: "translateY(-5px)",
											boxShadow: "0 20px 48px rgba(50,55,95,0.18)",
										},
									}}
								>
									<Box
											sx={{
											px: 2.5,
											pt: 2.5,
											pb: 2,
											background:
												"linear-gradient(180deg, rgba(255,255,255,0.75) 0%, rgba(255,255,255,0.4) 100%)",
										}}
									>
										<ProductImage src={row.imageUrl} alt={row.name} />
									</Box>
									<CardContent
											sx={{
											flex: 1,
											display: "flex",
											flexDirection: "column",
											gap: 1.2,
											pt: 2,
											alignItems: "center",
											textAlign: "center",
										}}
									>
										<Typography
											variant="subtitle1"
											fontWeight={700}
											sx={{
												minHeight: 48,
												display: "-webkit-box",
												WebkitLineClamp: 2,
												WebkitBoxOrient: "vertical",
												overflow: "hidden",
												width: "100%",
											}}
										>
											{row.name || "-"}
										</Typography>
										<Stack
											direction="row"
											spacing={1}
											useFlexGap
											flexWrap="wrap"
											justifyContent="center"
										>
											<Chip
												size="small"
												label={shopTypeName(row.shopType)}
												color="default"
												sx={{
													fontWeight: 600,
													color: "#ffffff",
													bgcolor: accent,
												}}
											/>
											{sizeText && (
												<Chip
													size="small"
													variant="outlined"
													label={`Size ${sizeText}`}
													sx={{ borderColor: accent, color: accent }}
												/>
											)}
										</Stack>
										<Box>
											<Typography
												variant="h6"
												component="p"
												fontWeight={700}
												sx={{ color: accent }}
											>
												{priceText}
											</Typography>
										</Box>
										{row.promoText && (
											<Typography
												variant="caption"
												sx={{ mt: 0.4, color: accent, fontWeight: 600 }}
											>
												Promo: {row.promoText}
											</Typography>
										)}
										<Box sx={{ minHeight: "1.5em" }}>
											{recommendationText && (
												<Typography
													variant="caption"
													fontWeight={600}
													className="recommendation-text"
													sx={{ textAlign: "center" }}
												>
													{recommendationText}
												</Typography>
											)}
										</Box>
										<Stack
											direction="row"
											spacing={1}
											sx={{ mt: "auto", alignSelf: "center" }}
											alignItems="center"
										>
											<Button
												className="compare-btn"
												variant="contained"
												size="small"
												onClick={() =>
													handleCompare(
														row.productId,
														row.name,
														row.shopType,
														row,
													)
												}
											>
												Compare
											</Button>
											<IconButton
												className="favorite-btn"
												size="small"
												onClick={() => void handleToggleFavorite(row.productId)}
												disabled={favoriteBusy}
												aria-label={
													isFavorite
														? "Remove from My Favorites"
														: "Add to My Favorites"
												}
												sx={{
													color: accent,
													border: "1px solid",
													borderColor: `${accent}33`,
													bgcolor: "#ffffff",
													"&:hover": {
														bgcolor: `${accent}12`,
													},
												}}
											>
												{isFavorite ? (
													<FavoriteIcon fontSize="small" />
												) : (
													<FavoriteBorderIcon fontSize="small" />
												)}
											</IconButton>
										</Stack>
									</CardContent>
								</Card>
							);
						})}
					</Box>
				)}
			</Box>

			<Stack
				direction={{ xs: "column", sm: "row" }}
				spacing={2}
				sx={{ mt: 2 }}
				alignItems={{ xs: "stretch", sm: "center" }}
				justifyContent="space-between"
			>
				<Typography variant="body2" color="text.secondary">
					{rowCount} products
				</Typography>
				<Stack
					direction={{ xs: "column", sm: "row" }}
					spacing={2}
					alignItems={{ xs: "stretch", sm: "center" }}
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
						size={isMdDown ? "small" : "medium"}
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
			</Stack>

			<CompareDialog
				open={compareOpen}
				keyword={compareKeyword}
				sourceShop={compareSourceShop}
				sourceProductId={compareSourceProductId}
				sourceProduct={compareSourceProduct}
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
