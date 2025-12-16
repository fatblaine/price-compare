import * as React from "react";
import {
	Box,
	Card,
	CardContent,
	CircularProgress,
	List,
	ListItem,
	ListItemText,
	Typography,
} from "@mui/material";
import { useTheme, useMediaQuery } from "@mui/material";
import { fetchFavorites, type FavoriteItem } from "./api/favorites";

// Basic page listing user's favorite products by ID.
// The backend currently returns product IDs only; name/price lookup can be added later.
export default function MyFavoritesPage() {
	const theme = useTheme();
	const isXs = useMediaQuery(theme.breakpoints.down("sm"));

	const [items, setItems] = React.useState<FavoriteItem[]>([]);
	const [loading, setLoading] = React.useState(false);

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
						This basic view shows the product IDs you have favorited.
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
								return (
									<ListItem key={fav.id}>
										<ListItemText
											primary={`Product #${fav.productId}`}
											secondary={
												createdText
													? `Added at ${createdText}`
													: undefined
											}
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

