import * as React from "react";
import {
	Box,
	Button,
	Card,
	CardContent,
	CircularProgress,
	List,
	ListItem,
	ListItemText,
	Stack,
	Typography,
} from "@mui/material";
import { useTheme, useMediaQuery } from "@mui/material";
import {
	fetchFavorites,
	removeFavorite,
	updateFavorite,
	type FavoriteItem,
} from "./api/favorites";

// Basic page listing user's favorite products by ID.
// The backend currently returns product IDs only; name/price lookup can be added later.
export default function MyFavoritesPage() {
	const theme = useTheme();
	const isXs = useMediaQuery(theme.breakpoints.down("sm"));

	const [items, setItems] = React.useState<FavoriteItem[]>([]);
	const [loading, setLoading] = React.useState(false);
	const [busyIds, setBusyIds] = React.useState<number[]>([]);

	const setBusy = React.useCallback((id: number, busy: boolean) => {
		setBusyIds((prev) => {
			if (busy) {
				return prev.includes(id) ? prev : [...prev, id];
			}
			return prev.filter((item) => item !== id);
		});
	}, []);

	React.useEffect(() => {
		let cancelled = false;
		const run = async () => {
			setLoading(true);
			try {
				const data = await fetchFavorites();
				if (!cancelled) setItems(data);
			} catch (e) {
				// eslint-disable-next-line no-console
				console.error("Failed to load favorites", e);
			} finally {
				if (!cancelled) setLoading(false);
			}
		};
		void run();
		return () => {
			cancelled = true;
		};
	}, []);

	const handleRemove = React.useCallback(
		async (fav: FavoriteItem) => {
			setBusy(fav.id, true);
			try {
				await removeFavorite(fav.productId);
				setItems((prev) => prev.filter((item) => item.id !== fav.id));
			} catch (e) {
				// eslint-disable-next-line no-console
				console.error("Failed to remove favorite", e);
			} finally {
				setBusy(fav.id, false);
			}
		},
		[setBusy],
	);

	const handleToggleAlerts = React.useCallback(
		async (fav: FavoriteItem) => {
			const nextActive = !fav.isActive;
			setBusy(fav.id, true);
			try {
				await updateFavorite(fav.productId, nextActive);
				setItems((prev) =>
					prev.map((item) =>
						item.id === fav.id
							? { ...item, isActive: nextActive }
							: item,
					),
				);
			} catch (e) {
				// eslint-disable-next-line no-console
				console.error("Failed to update favorite alerts", e);
			} finally {
				setBusy(fav.id, false);
			}
		},
		[setBusy],
	);

	return (
		<Box>
			<Typography
				variant={isXs ? "h5" : "h4"}
				fontWeight={800}
				sx={{ mb: 2 }}
			>
				My Favorites
			</Typography>

			<Card variant="outlined">
				<CardContent sx={{ p: { xs: 2, sm: 3 } }}>
					<Typography variant="subtitle1" fontWeight={600} gutterBottom>
						Favorite products
					</Typography>
	<Typography variant="body2" color="text.secondary" sx={{ mb: 2 }}>
		This view shows the product names you have favorited.
	</Typography>

					{loading ? (
						<Box
							sx={{
								display: "flex",
								alignItems: "center",
								justifyContent: "center",
								minHeight: 160,
							}}
						>
							<CircularProgress size={24} />
						</Box>
					) : items.length === 0 ? (
						<Typography variant="body2" color="text.secondary">
							You have not added any favorites yet.
						</Typography>
					) : (
						<List dense>
							{items.map((fav) => {
								const createdText = fav.createdAt
									? new Date(fav.createdAt).toLocaleString()
									: "";
								const statusText = fav.isActive
									? "Alerts on"
									: "Alerts off";
								const secondaryParts = [];
								if (createdText) {
									secondaryParts.push(`Added at ${createdText}`);
								}
								secondaryParts.push(statusText);
								const secondaryText = secondaryParts.join(" • ");
								const isBusy = busyIds.includes(fav.id);
								return (
									<ListItem
										key={fav.id}
										secondaryAction={
											<Stack direction="row" spacing={1}>
												<Button
													size="small"
													variant="contained"
													onClick={() => void handleToggleAlerts(fav)}
													disabled={isBusy}
												>
													{fav.isActive
														? "Disable alerts"
														: "Enable alerts"}
												</Button>
												<Button
													size="small"
													variant="text"
													color="error"
													sx={{ textTransform: "uppercase" }}
													onClick={() => void handleRemove(fav)}
													disabled={isBusy}
												>
													Delete
												</Button>
											</Stack>
										}
									>
										<ListItemText
											primary={
												fav.productName || `Product ${fav.productId}`
											}
											secondary={secondaryText}
										/>
									</ListItem>
								);
							})}
						</List>
					)}
				</CardContent>
			</Card>
		</Box>
	);
}
