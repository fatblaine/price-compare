import * as React from "react";
import {
	Avatar,
	Box,
	Button,
	ButtonBase,
	CircularProgress,
	IconButton,
	Snackbar,
	Typography,
} from "@mui/material";
import { useTheme, useMediaQuery } from "@mui/material";
import { useNavigate } from "react-router-dom";
import NotificationsIcon from "@mui/icons-material/Notifications";
import NotificationsOffOutlinedIcon from "@mui/icons-material/NotificationsOffOutlined";
import DeleteOutlineIcon from "@mui/icons-material/DeleteOutline";
import EventIcon from "@mui/icons-material/Event";
import ChevronRightIcon from "@mui/icons-material/ChevronRight";
import {
	addFavorite,
	fetchFavorites,
	removeFavorite,
	updateFavorite,
	type FavoriteItem,
} from "./api/favorites";
import { fetchUpcomingByProducts, type UpcomingSpecial } from "./api/upcoming";
import { favoriteUpcomingParts } from "./utils/waitAdvice";
import ConfirmSheet from "./components/ConfirmSheet";
import { DISPLAY_FONT, tokens } from "./theme";

type Filter = "all" | "specials" | "off";

/** "2 Nov" this year, "2 Nov 2025" otherwise. */
function shortDate(iso?: string | null): string {
	if (!iso) return "";
	const d = new Date(iso);
	if (Number.isNaN(d.getTime())) return "";
	const sameYear = d.getFullYear() === new Date().getFullYear();
	return d.toLocaleDateString("en-AU", {
		day: "numeric",
		month: "short",
		...(sameYear ? {} : { year: "numeric" }),
	});
}

function initialOf(name?: string | null): string {
	return (name ?? "").replace(/[^A-Za-z]/g, "").charAt(0).toUpperCase() || "?";
}

/** Product thumbnail with the first letter as fallback (the favourites API has image URLs for most items). */
function Thumb({ src, name, size }: { src?: string | null; name: string; size: number }) {
	return (
		<Avatar
			variant="rounded"
			src={src ?? undefined}
			alt=""
			sx={{
				width: size,
				height: size,
				flexShrink: 0,
				borderRadius: size >= 64 ? "12px" : "10px",
				bgcolor: tokens.imageGround,
				border: `1px solid ${tokens.line}`,
				color: tokens.ink2,
				fontFamily: DISPLAY_FONT,
				fontWeight: 800,
				fontSize: Math.round(size * 0.34),
				"& img": { objectFit: "contain", p: "4px", boxSizing: "border-box" },
			}}
		>
			{initialOf(name)}
		</Avatar>
	);
}

// Favourite products: next-week specials, price-drop alert switch, remove with undo (BTS design 2026-11-19).
export default function MyFavoritesPage() {
	const theme = useTheme();
	const isXs = useMediaQuery(theme.breakpoints.down("sm"));
	const navigate = useNavigate();

	const [items, setItems] = React.useState<FavoriteItem[]>([]);
	const [loading, setLoading] = React.useState(false);
	const [loadError, setLoadError] = React.useState(false);
	const [busyIds, setBusyIds] = React.useState<number[]>([]);
	const [filter, setFilter] = React.useState<Filter>("all");
	const [pendingDelete, setPendingDelete] = React.useState<FavoriteItem | null>(null);
	const [removed, setRemoved] = React.useState<FavoriteItem | null>(null);
	const [actionError, setActionError] = React.useState<string | null>(null);

	// BTS-163: next-week catalogue offers for the favourited products.
	const [upcoming, setUpcoming] = React.useState<Record<string, UpcomingSpecial | null>>({});
	const productIdsKey = items.map((f) => f.productId).join(",");
	React.useEffect(() => {
		const ids = productIdsKey ? productIdsKey.split(",") : [];
		if (ids.length === 0) {
			setUpcoming({});
			return;
		}
		let cancelled = false;
		fetchUpcomingByProducts(ids)
			.then((map) => {
				if (!cancelled) setUpcoming(map);
			})
			.catch(() => {
				// Optional information: the list works without it.
			});
		return () => {
			cancelled = true;
		};
	}, [productIdsKey]);

	const setBusy = React.useCallback((id: number, busy: boolean) => {
		setBusyIds((prev) => {
			if (busy) return prev.includes(id) ? prev : [...prev, id];
			return prev.filter((item) => item !== id);
		});
	}, []);

	const load = React.useCallback(async (showSpinner: boolean) => {
		if (showSpinner) setLoading(true);
		setLoadError(false);
		try {
			setItems(await fetchFavorites());
		} catch (e) {
			// eslint-disable-next-line no-console
			console.error("Failed to load favorites", e);
			setLoadError(true);
		} finally {
			if (showSpinner) setLoading(false);
		}
	}, []);

	React.useEffect(() => {
		void load(true);
	}, [load]);

	const handleToggleAlerts = React.useCallback(
		async (fav: FavoriteItem) => {
			const nextActive = !fav.isActive;
			setBusy(fav.id, true);
			try {
				await updateFavorite(fav.productId, nextActive);
				setItems((prev) =>
					prev.map((item) => (item.id === fav.id ? { ...item, isActive: nextActive } : item)),
				);
			} catch (e) {
				// eslint-disable-next-line no-console
				console.error("Failed to update favorite alerts", e);
				setActionError("Couldn't change the alert. Please try again.");
			} finally {
				setBusy(fav.id, false);
			}
		},
		[setBusy],
	);

	const confirmBusy = pendingDelete ? busyIds.includes(pendingDelete.id) : false;

	const handleConfirmRemove = React.useCallback(async () => {
		const fav = pendingDelete;
		if (!fav) return;
		setBusy(fav.id, true);
		try {
			await removeFavorite(fav.productId);
			setItems((prev) => prev.filter((item) => item.id !== fav.id));
			setRemoved(fav);
			setPendingDelete(null);
		} catch (e) {
			// eslint-disable-next-line no-console
			console.error("Failed to remove favorite", e);
			setActionError("Couldn't remove it. Please try again.");
			setPendingDelete(null);
		} finally {
			setBusy(fav.id, false);
		}
	}, [pendingDelete, setBusy]);

	// Undo = favourite it again, restore the alert setting, and reload to pick up the new row id.
	const handleUndo = React.useCallback(async () => {
		const fav = removed;
		setRemoved(null);
		if (!fav) return;
		try {
			await addFavorite(fav.productId);
			if (!fav.isActive) await updateFavorite(fav.productId, false);
			await load(false);
		} catch (e) {
			// eslint-disable-next-line no-console
			console.error("Failed to restore favorite", e);
			setActionError("Couldn't restore it. Add it again from Products.");
		}
	}, [removed, load]);

	const rows = React.useMemo(
		() =>
			items.map((fav) => ({
				fav,
				special: favoriteUpcomingParts(upcoming[fav.productId]),
			})),
		[items, upcoming],
	);
	const alertCount = rows.filter((r) => r.fav.isActive).length;
	const specialCount = rows.filter((r) => r.special).length;
	const offCount = rows.length - alertCount;
	const firstSpecialDate = rows.find((r) => r.special)?.special?.startDate;
	const shown = rows.filter(
		(r) =>
			filter === "all" ||
			(filter === "specials" && r.special) ||
			(filter === "off" && !r.fav.isActive),
	);

	const filters: { key: Filter; label: string; count: number }[] = [
		{ key: "all", label: "All", count: rows.length },
		{ key: "specials", label: "On special next week", count: specialCount },
		{ key: "off", label: "Alerts off", count: offCount },
	];

	const filterChips = (
		<Box
			role="group"
			aria-label="Filter favourites"
			sx={{
				display: "flex",
				gap: 1,
				overflowX: { xs: "auto", sm: "visible" },
				flexWrap: { xs: "nowrap", sm: "wrap" },
				mx: { xs: -2, sm: 0 },
				px: { xs: 2, sm: 0 },
				scrollbarWidth: "none",
			}}
		>
			{filters.map((f) => {
				const selected = filter === f.key;
				return (
					<ButtonBase
						key={f.key}
						aria-pressed={selected}
						onClick={() => setFilter(f.key)}
						sx={{
							flexShrink: 0,
							height: 40,
							px: { xs: 1.75, sm: 2 },
							borderRadius: 999,
							border: `1px solid ${selected ? tokens.ink : tokens.line}`,
							bgcolor: selected ? tokens.ink : tokens.surface,
							color: selected ? "#ffffff" : tokens.ink2,
							fontSize: { xs: 13, sm: 14 },
							fontWeight: 600,
							whiteSpace: "nowrap",
							gap: 0.75,
							"&:hover": { borderColor: selected ? tokens.ink : tokens.lineStrong },
							"&.Mui-focusVisible": { outline: `2px solid ${tokens.brand}`, outlineOffset: 2 },
						}}
					>
						{f.label}
						<Box component="span" sx={{ opacity: 0.8, fontVariantNumeric: "tabular-nums" }}>
							{f.count}
						</Box>
					</ButtonBase>
				);
			})}
		</Box>
	);

	return (
		<Box sx={{ display: "flex", flexDirection: "column", gap: { xs: 1.5, sm: 2.25 } }}>
			<Box
				sx={{
					display: "flex",
					flexWrap: "wrap",
					alignItems: "flex-end",
					justifyContent: "space-between",
					gap: 2,
				}}
			>
				<Box>
					<Typography
						component="h1"
						sx={{
							fontFamily: DISPLAY_FONT,
							fontWeight: 800,
							fontSize: { xs: 24, sm: 34 },
							letterSpacing: { xs: "-0.02em", sm: "-0.03em" },
							lineHeight: 1.12,
						}}
					>
						My Favorites
					</Typography>
					{rows.length > 0 && (
						<Typography sx={{ mt: 0.5, fontSize: { xs: 13, sm: 15 }, color: tokens.ink2 }}>
							<Box component="strong" sx={{ color: tokens.ink, fontWeight: 700 }}>
								{rows.length}
							</Box>{" "}
							saved ·{" "}
							<Box component="strong" sx={{ color: tokens.ink, fontWeight: 700 }}>
								{alertCount}
							</Box>{" "}
							with price-drop alerts
						</Typography>
					)}
				</Box>
				{!isXs && rows.length > 0 && filterChips}
			</Box>

			{specialCount > 0 && (
				<Box
					sx={{
						display: "flex",
						flexWrap: "wrap",
						alignItems: "center",
						gap: { xs: 1.5, sm: 2 },
						p: { xs: 1.5, sm: 2 },
						bgcolor: tokens.surface,
						border: `1px solid ${tokens.line}`,
						borderRadius: "16px",
						boxShadow: tokens.cardShadow,
					}}
				>
					<Box
						sx={{
							width: { xs: 40, sm: 44 },
							height: { xs: 40, sm: 44 },
							flexShrink: 0,
							borderRadius: "12px",
							bgcolor: tokens.promoTint,
							color: tokens.promo,
							display: "flex",
							alignItems: "center",
							justifyContent: "center",
						}}
					>
						<EventIcon aria-hidden="true" sx={{ fontSize: 22 }} />
					</Box>
					<Box sx={{ flex: "1 1 180px", minWidth: 0 }}>
						<Typography sx={{ fontSize: { xs: 14, sm: 15.5 }, fontWeight: 700 }}>
							{specialCount === 1
								? "1 favourite goes on special next week"
								: `${specialCount} favourites go on special next week`}
						</Typography>
						<Typography sx={{ fontSize: { xs: 12.5, sm: 13.5 }, color: tokens.ink2 }}>
							Next week's catalogue{firstSpecialDate ? ` · from ${firstSpecialDate}` : ""}
						</Typography>
					</Box>
					<Button
						variant="outlined"
						onClick={() => setFilter("specials")}
						endIcon={isXs ? undefined : <ChevronRightIcon />}
						aria-label={isXs ? "Show favourites on special next week" : undefined}
						sx={{ height: 44, minWidth: 44, px: { xs: 1, sm: 2.25 } }}
					>
						{isXs ? <ChevronRightIcon /> : "Show them"}
					</Button>
				</Box>
			)}

			{isXs && rows.length > 0 && filterChips}

			{loading ? (
				<Box sx={{ display: "flex", justifyContent: "center", py: 8 }}>
					<CircularProgress size={28} />
				</Box>
			) : loadError ? (
				<EmptyBox>
					Couldn't load your favourites.{" "}
					<Button onClick={() => void load(true)} sx={{ height: 40 }}>
						Try again
					</Button>
				</EmptyBox>
			) : rows.length === 0 ? (
				<EmptyBox>
					<Typography sx={{ fontWeight: 700, color: tokens.ink, mb: 0.5 }}>No favourites yet</Typography>
					Tap the heart on any product to get price-drop alerts and next-week specials here.
					<Box sx={{ mt: 2 }}>
						<Button variant="contained" onClick={() => navigate("/products")} sx={{ height: 44 }}>
							Browse products
						</Button>
					</Box>
				</EmptyBox>
			) : shown.length === 0 ? (
				<EmptyBox>
					Nothing here.{" "}
					<Button onClick={() => setFilter("all")} sx={{ height: 40 }}>
						Show all favourites
					</Button>
				</EmptyBox>
			) : (
				<Box
					component="ul"
					sx={{
						listStyle: "none",
						m: 0,
						p: 0,
						display: "grid",
						gridTemplateColumns: { xs: "minmax(0, 1fr)", md: "repeat(2, minmax(0, 1fr))" },
						gap: { xs: 1, sm: 1.5 },
					}}
				>
					{shown.map(({ fav, special }) => {
						const isBusy = busyIds.includes(fav.id);
						const name = fav.productName || `Product ${fav.productId}`;
						const added = shortDate(fav.createdAt);
						return (
							<Box
								component="li"
								key={fav.id}
								className="favorite-row"
								sx={{
									display: "flex",
									alignItems: "center",
									gap: { xs: 1.25, sm: 1.75 },
									p: { xs: 1.25, sm: 1.5 },
									bgcolor: tokens.surface,
									border: `1px solid ${tokens.line}`,
									borderRadius: { xs: "14px", sm: "16px" },
									boxShadow: "0 1px 2px rgba(20,22,30,.04)",
									minWidth: 0,
								}}
							>
								<Thumb src={fav.imageUrl} name={name} size={isXs ? 56 : 72} />
								<Box sx={{ flex: 1, minWidth: 0, display: "flex", flexDirection: "column", gap: 0.5 }}>
									<Typography
										sx={{
											fontSize: { xs: 14, sm: 15 },
											fontWeight: 600,
											lineHeight: 1.3,
											display: "-webkit-box",
											WebkitLineClamp: 2,
											WebkitBoxOrient: "vertical",
											overflow: "hidden",
										}}
									>
										{name}
									</Typography>
									<Box
										sx={{
											display: "flex",
											flexWrap: "wrap",
											alignItems: "center",
											gap: { xs: "4px 6px", sm: "6px 10px" },
											fontSize: { xs: 12, sm: 12.5 },
											color: tokens.ink2,
										}}
									>
										{special && (
											<>
												<Box
													component="span"
													className="next-week-text"
													sx={{
														display: "inline-flex",
														alignItems: "center",
														gap: 0.5,
														px: 1,
														py: "3px",
														borderRadius: 999,
														bgcolor: tokens.promoTint,
														color: tokens.promo,
														fontSize: { xs: 11.5, sm: 12 },
														fontWeight: 700,
														whiteSpace: "nowrap",
													}}
												>
													<EventIcon aria-hidden="true" sx={{ fontSize: 13 }} />
													{special.startDate} · {special.price}
												</Box>
												<Box component="span" sx={{ color: tokens.save, fontWeight: 700 }}>
													Save {special.save}
												</Box>
											</>
										)}
										{added && <span>Added {added}</span>}
										{isXs && !fav.isActive && <span>· Alerts off</span>}
									</Box>
								</Box>
								<ButtonBase
									className="favorite-alert-btn"
									onClick={() => void handleToggleAlerts(fav)}
									disabled={isBusy}
									aria-pressed={fav.isActive}
									aria-label={`${fav.isActive ? "Turn off" : "Turn on"} price alerts for ${name}`}
									sx={{
										flexShrink: 0,
										height: { xs: 44, sm: 40 },
										minWidth: 44,
										px: { xs: 0, sm: 1.5 },
										gap: 0.75,
										borderRadius: { xs: "12px", sm: 999 },
										border: `1px solid ${fav.isActive ? tokens.brandTint : tokens.line}`,
										bgcolor: fav.isActive ? tokens.brandTint : tokens.surface,
										color: fav.isActive ? tokens.brand : tokens.ink2,
										fontSize: 13,
										fontWeight: 600,
										whiteSpace: "nowrap",
										opacity: isBusy ? 0.6 : 1,
										"&.Mui-focusVisible": { outline: `2px solid ${tokens.brand}`, outlineOffset: 2 },
									}}
								>
									{fav.isActive ? (
										<NotificationsIcon aria-hidden="true" sx={{ fontSize: 19 }} />
									) : (
										<NotificationsOffOutlinedIcon aria-hidden="true" sx={{ fontSize: 19 }} />
									)}
									{!isXs && (fav.isActive ? "Alerts on" : "Alerts off")}
								</ButtonBase>
								<IconButton
									onClick={() => setPendingDelete(fav)}
									disabled={isBusy}
									aria-label={`Remove ${name} from favourites`}
									title="Remove from favourites"
									sx={{
										width: 44,
										height: 44,
										ml: { xs: -0.5, sm: -0.75 },
										borderRadius: "12px",
										color: tokens.ink2,
										"&:hover": { color: "#B42318", bgcolor: "#FDECEC" },
									}}
								>
									<DeleteOutlineIcon sx={{ fontSize: 20 }} />
								</IconButton>
							</Box>
						);
					})}
				</Box>
			)}

			<ConfirmSheet
				open={Boolean(pendingDelete)}
				title="Remove from favourites?"
				confirmLabel="Remove"
				cancelLabel="Keep it"
				busy={confirmBusy}
				onClose={() => setPendingDelete(null)}
				onConfirm={() => void handleConfirmRemove()}
			>
				{pendingDelete && (
					<>
						<Box
							sx={{
								display: "flex",
								alignItems: "center",
								gap: 1.5,
								p: 1.25,
								mb: 1.5,
								border: `1px solid ${tokens.line}`,
								borderRadius: "12px",
							}}
						>
							<Thumb src={pendingDelete.imageUrl} name={pendingDelete.productName} size={44} />
							<Typography sx={{ fontSize: 14.5, fontWeight: 600, lineHeight: 1.3 }}>
								{pendingDelete.productName || `Product ${pendingDelete.productId}`}
							</Typography>
						</Box>
						<Typography sx={{ fontSize: 14, lineHeight: 1.5, color: tokens.ink2 }}>
							You'll stop getting price-drop alerts for this product. You can favourite it again from
							Products.
						</Typography>
					</>
				)}
			</ConfirmSheet>

			<Snackbar
				open={Boolean(removed)}
				autoHideDuration={6000}
				onClose={(_, reason) => {
					if (reason !== "clickaway") setRemoved(null);
				}}
				message={removed ? `Removed ${removed.productName || "favourite"}` : ""}
				action={
					<Button onClick={() => void handleUndo()} sx={{ color: "#C7CAFF", fontWeight: 700, height: 40 }}>
						Undo
					</Button>
				}
				anchorOrigin={{ vertical: "bottom", horizontal: isXs ? "center" : "left" }}
				sx={{ bottom: { xs: "calc(76px + env(safe-area-inset-bottom))", sm: 24 } }}
			/>
			<Snackbar
				open={Boolean(actionError)}
				autoHideDuration={5000}
				onClose={() => setActionError(null)}
				message={actionError ?? ""}
				anchorOrigin={{ vertical: "bottom", horizontal: isXs ? "center" : "left" }}
				sx={{ bottom: { xs: "calc(76px + env(safe-area-inset-bottom))", sm: 24 } }}
			/>
		</Box>
	);
}

function EmptyBox({ children }: { children: React.ReactNode }) {
	return (
		<Box
			sx={{
				py: { xs: 4, sm: 6 },
				px: 2,
				textAlign: "center",
				bgcolor: tokens.surface,
				border: `1px dashed ${tokens.lineStrong}`,
				borderRadius: "16px",
				color: tokens.ink2,
				fontSize: { xs: 13.5, sm: 14.5 },
				lineHeight: 1.5,
			}}
		>
			{children}
		</Box>
	);
}
