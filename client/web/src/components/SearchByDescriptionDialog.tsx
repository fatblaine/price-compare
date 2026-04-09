import React, { useState } from "react";
import {
	Dialog,
	DialogTitle,
	DialogContent,
	DialogActions,
	Button,
	TextField,
	Box,
	Typography,
	Chip,
	CircularProgress,
	Card,
	CardMedia,
	CardContent,
	CardActions,
	Alert,
	IconButton,
	FormControl,
	InputLabel,
	Select,
	MenuItem,
} from "@mui/material";
import CloseIcon from "@mui/icons-material/Close";
import {
	searchByDescription,
	type DescriptionSearchResponse,
	type ProductSearchItem,
} from "../api/products";

function shopName(shopType?: number | null): string {
	if (shopType === 0) return "Coles";
	if (shopType === 1) return "Woolworths";
	return "Unknown";
}

function shopColor(shopType?: number | null): string {
	if (shopType === 0) return "#c93925";
	if (shopType === 1) return "#3f8149";
	return "#555";
}

interface Props {
	open: boolean;
	onClose: () => void;
	onSelect: (productName: string) => void;
	onQuotaUpdate?: (remaining: number) => void;
}

export default function SearchByDescriptionDialog({ open, onClose, onSelect, onQuotaUpdate }: Props) {
	const [query, setQuery] = useState("");
	const [shopType, setShopType] = useState<number | "">("");
	const [loading, setLoading] = useState(false);
	const [result, setResult] = useState<DescriptionSearchResponse | null>(null);
	const [error, setError] = useState<string | null>(null);
	const [rateLimitHit, setRateLimitHit] = useState(false);

	const handleSearch = async () => {
		if (!query.trim()) return;
		setLoading(true);
		setError(null);
		setResult(null);
		setRateLimitHit(false);

		try {
			const data = await searchByDescription(query.trim(), 10, shopType === "" ? undefined : shopType);
			setResult(data);
			onQuotaUpdate?.(data.remainingSearches);
		} catch (err: unknown) {
			if (err instanceof Error && (err as Error & { status?: number }).status === 429) {
				setRateLimitHit(true);
				onQuotaUpdate?.(0);
			} else {
				setError("Search failed. Please try again.");
			}
		} finally {
			setLoading(false);
		}
	};

	const handleKeyDown = (e: React.KeyboardEvent) => {
		if (e.key === "Enter" && !loading) void handleSearch();
	};

	const handleSelect = (product: ProductSearchItem) => {
		onSelect(product.name);
		handleClose();
	};

	const handleClose = () => {
		setQuery("");
		setShopType("");
		setResult(null);
		setError(null);
		setRateLimitHit(false);
		onClose();
	};

	const isExhausted = result?.remainingSearches === 0 || rateLimitHit;

	return (
		<Dialog open={open} onClose={handleClose} maxWidth="sm" fullWidth>
			<DialogTitle sx={{ display: "flex", justifyContent: "space-between", alignItems: "center" }}>
				AI Product Search
				<IconButton size="small" onClick={handleClose}>
					<CloseIcon />
				</IconButton>
			</DialogTitle>

			<DialogContent>
				<Box sx={{ display: "flex", gap: 1, mb: 1 }}>
					<TextField
						autoFocus
						fullWidth
						multiline
						maxRows={3}
					placeholder="Describe what you're looking for… (any language)"
					value={query}
					onChange={(e) => setQuery(e.target.value)}
						onKeyDown={handleKeyDown}
						disabled={loading || rateLimitHit}
					/>
					<FormControl size="small" sx={{ minWidth: 140 }}>
						<InputLabel>Shop</InputLabel>
						<Select
							value={shopType}
							label="Shop"
							onChange={(e) => setShopType(e.target.value as number | "")}
							disabled={loading || rateLimitHit}
						>
							<MenuItem value="">All shops</MenuItem>
							<MenuItem value={0}>Coles</MenuItem>
							<MenuItem value={1}>Woolworths</MenuItem>
						</Select>
					</FormControl>
				</Box>

				{result && (
					<Typography variant="caption" color="text.secondary" sx={{ display: "block", mb: 1 }}>
						{3 - result.remainingSearches} / 3 AI searches used today
						{result.remainingSearches === 0 && " — daily limit reached"}
					</Typography>
				)}

				{rateLimitHit && (
					<Alert severity="warning" sx={{ mb: 1 }}>
						You've reached your daily limit of 3 AI searches. Please try again tomorrow.
					</Alert>
				)}

				{error && (
					<Alert severity="error" sx={{ mb: 1 }}>
						{error}
					</Alert>
				)}

				{loading && (
					<Box sx={{ display: "flex", justifyContent: "center", py: 3 }}>
						<CircularProgress />
					</Box>
				)}

				{result && !loading && (
					<>
						{result.inferredProducts.length > 0 && (
							<Box sx={{ display: "flex", flexWrap: "wrap", gap: 0.5, mb: 2 }}>
								<Typography variant="caption" color="text.secondary" sx={{ mr: 0.5, alignSelf: "center" }}>
									Searched for:
								</Typography>
								{result.inferredProducts.map((kw) => (
									<Chip key={kw} label={kw} size="small" variant="outlined" />
								))}
							</Box>
						)}

						{result.products.length === 0 ? (
							<Typography color="text.secondary" textAlign="center" sx={{ py: 2 }}>
								No products found. Try a different description.
							</Typography>
						) : (
							<Box sx={{ display: "flex", flexDirection: "column", gap: 1 }}>
								{result.products.map((product) => (
									<Card key={product.productId} variant="outlined" sx={{ display: "flex" }}>
										{product.imageUrl && (
											<CardMedia
												component="img"
												sx={{ width: 64, height: 64, objectFit: "contain", p: 1 }}
												image={product.imageUrl}
												alt={product.name}
											/>
										)}
										<CardContent sx={{ flex: 1, py: 1, "&:last-child": { pb: 1 } }}>
											<Typography
												variant="caption"
												sx={{ color: shopColor(product.shopType), fontWeight: 700 }}
											>
												{shopName(product.shopType)}
											</Typography>
											<Typography variant="body2" fontWeight={600} lineHeight={1.3}>
												{product.name}
											</Typography>
											{product.sizeValue != null && product.sizeUnit && (
												<Typography variant="caption" color="text.secondary">
													{product.sizeValue} {product.sizeUnit}
												</Typography>
											)}
											{product.price != null && (
												<Typography variant="body2" color="primary" fontWeight={700}>
													${product.price.toFixed(2)}
													{product.promoText && (
														<Typography component="span" variant="caption" color="text.secondary" sx={{ ml: 0.5 }}>
															{product.promoText}
														</Typography>
													)}
												</Typography>
											)}
										</CardContent>
										<CardActions sx={{ pr: 1 }}>
											<Button size="small" variant="contained" onClick={() => handleSelect(product)}>
												Select
											</Button>
										</CardActions>
									</Card>
								))}
							</Box>
						)}
					</>
				)}
			</DialogContent>

			<DialogActions>
				<Button onClick={handleClose} color="inherit">
					Cancel
				</Button>
				<Button
					onClick={() => void handleSearch()}
					variant="contained"
					disabled={loading || !query.trim() || isExhausted}
				>
					{loading ? "Searching…" : "Search"}
				</Button>
			</DialogActions>
		</Dialog>
	);
}
