import {
	AppBar,
	BottomNavigation,
	BottomNavigationAction,
	Box,
	Button,
	CircularProgress,
	Container,
	Dialog,
	DialogActions,
	DialogContent,
	DialogContentText,
	DialogTitle,
	Paper,
	Tab,
	Tabs,
	Toolbar,
	Typography,
} from "@mui/material";
import React from "react";
import {
	Navigate,
	Route,
	Routes,
	useLocation,
	useNavigate,
} from "react-router-dom";
import { useAuth } from "react-oidc-context";
import axios from "axios";
import ProductsPage from "./ProductsPage";
import LoginPage from "./LoginPage";
import CallbackPage from "./CallbackPage";
import { clearGuestSession, getStoredGuest, setGuestSession } from "./api/auth";
import { buildCognitoLogoutUrl } from "./auth/oidcConfig";
import { fetchAdminWhoAmI } from "./api/admin";
import MyReceiptsPage from "./MyReceiptsPage";
import MyFavoritesPage from "./MyFavoritesPage";
import AdminJobsPage from "./AdminJobsPage";
import StorefrontIcon from "@mui/icons-material/Storefront";
import ReceiptLongIcon from "@mui/icons-material/ReceiptLong";
import FavoriteIcon from "@mui/icons-material/Favorite";
import AdminPanelSettingsIcon from "@mui/icons-material/AdminPanelSettings";

/** Height of the phone bottom navigation bar (MUI BottomNavigation default). */
const BOTTOM_NAV_HEIGHT = 56;

function App() {
	const auth = useAuth();
	const [guestMode, setGuestMode] = React.useState(getStoredGuest);
	const [isAdmin, setIsAdmin] = React.useState(false);
	const [restrictedDialogOpen, setRestrictedDialogOpen] = React.useState(false);
	const [pendingProtectedTab, setPendingProtectedTab] = React.useState<
		"receipts" | "favorites" | "admin" | null
	>(null);
	const location = useLocation();
	const navigate = useNavigate();

	const isAuthenticated = auth.isAuthenticated;
	const userEmail = auth.user?.profile?.email as string | undefined;
	const isGuest = guestMode && !isAuthenticated;
	const showTabs = isAuthenticated || isGuest;

	// Keep axios Authorization header in sync with the OIDC id_token
	React.useEffect(() => {
		const token = auth.user?.id_token;
		if (token) {
			axios.defaults.headers.common.Authorization = `Bearer ${token}`;
			clearGuestSession();
			setGuestMode(false);
		} else if (!auth.isLoading) {
			// eslint-disable-next-line @typescript-eslint/no-dynamic-delete
			delete axios.defaults.headers.common.Authorization;
		}
	}, [auth.user, auth.isLoading]);

	// Check admin status whenever auth changes
	React.useEffect(() => {
		let active = true;
		if (!isAuthenticated) {
			setIsAdmin(false);
			return;
		}
		(async () => {
			try {
				await fetchAdminWhoAmI();
				if (active) setIsAdmin(true);
			} catch {
				if (active) setIsAdmin(false);
			}
		})();
		return () => {
			active = false;
		};
	}, [isAuthenticated]);

	const mainTab = React.useMemo(() => {
		const path = location.pathname.toLowerCase();
		if (path.startsWith("/admin")) return "admin";
		if (path.startsWith("/receipts")) return "receipts";
		if (path.startsWith("/favorites")) return "favorites";
		return "products";
	}, [location.pathname]);

	const adminDefaultPath = "/admin/schedules";
	const adminPath = location.pathname.startsWith("/admin/")
		? location.pathname
		: adminDefaultPath;
	const tabToPath = (
		tab: "products" | "receipts" | "favorites" | "admin",
	) => (tab === "admin" ? adminDefaultPath : `/${tab}`);

	const handleLogout = async () => {
		clearGuestSession();
		setGuestMode(false);
		await auth.removeUser(); // Clear OIDC session locally
		window.location.href = buildCognitoLogoutUrl(); // Redirect to Cognito logout
	};

	const handleGuestLogin = () => {
		setGuestSession();
		setGuestMode(true);
		navigate("/products", { replace: true });
		setPendingProtectedTab(null);
	};

	const handleTabChange = (
		_: React.SyntheticEvent,
		val: "products" | "receipts" | "favorites" | "admin",
	) => {
		if (val === "admin" && !isAdmin) return;
		if (val !== "products" && !isAuthenticated) {
			setPendingProtectedTab(val);
			setRestrictedDialogOpen(true);
			return;
		}
		const targetPath = val === "admin" ? adminPath : `/${val}`;
		navigate(targetPath);
	};

	const handleCloseRestrictedDialog = () => {
		setRestrictedDialogOpen(false);
		setPendingProtectedTab(null);
	};

	const handleRestrictedSignIn = () => {
		setRestrictedDialogOpen(false);
		auth.signinRedirect();
	};

	const restrictedTarget =
		pendingProtectedTab === "receipts"
			? "My Receipts"
			: pendingProtectedTab === "favorites"
				? "My Favorites"
				: pendingProtectedTab === "admin"
					? "Admin"
					: "this section";

	const renderMainContent = () => {
		// /callback must always be reachable (Cognito redirects here after login)
		if (location.pathname === "/callback") {
			return <CallbackPage />;
		}

		// OIDC is still initialising (e.g. processing the auth code on page load)
		if (auth.isLoading) {
			return (
				<Box sx={{ display: "flex", justifyContent: "center", py: 8 }}>
					<CircularProgress />
				</Box>
			);
		}

		// Not signed in and not a guest → show login card
		if (!isAuthenticated && !isGuest) {
			return <LoginPage onGuestLogin={handleGuestLogin} />;
		}

		return (
			<Routes>
				<Route path="/" element={<Navigate to="/products" replace />} />
				<Route path="/callback" element={<CallbackPage />} />
				<Route path="/products" element={<ProductsPage />} />
				<Route
					path="/receipts"
					element={
						isAuthenticated ? (
							<MyReceiptsPage />
						) : (
							<Navigate to="/products" replace />
						)
					}
				/>
				<Route
					path="/favorites"
					element={
						isAuthenticated ? (
							<MyFavoritesPage />
						) : (
							<Navigate to="/products" replace />
						)
					}
				/>
				<Route
					path="/admin/*"
					element={
						isAuthenticated && isAdmin ? (
							<AdminJobsPage />
						) : (
							<Navigate to="/products" replace />
						)
					}
				/>
				<Route path="*" element={<Navigate to="/products" replace />} />
			</Routes>
		);
	};

	return (
		<Box sx={{ bgcolor: "transparent", minHeight: "100vh" }}>
			<AppBar
				position="sticky"
				elevation={0}
				sx={{
					// Phones: scroll away with the page instead of pinning the header.
					position: { xs: "static", md: "sticky" },
					background: { xs: "rgba(255,255,255,0.9)", md: "rgba(255,255,255,0.55)" },
					backdropFilter: "blur(30px) saturate(180%)",
					WebkitBackdropFilter: "blur(30px) saturate(180%)",
					borderBottom: "0.5px solid rgba(255,255,255,0.6)",
					boxShadow: "0 1px 0 rgba(0,0,0,0.04)",
				}}
			>
				<Toolbar sx={{ py: { xs: 0.5, sm: 1.5 }, px: { xs: 0 } }}>
					<Container
						maxWidth="lg"
						sx={{
							display: "flex",
							alignItems: "center",
							// Phones: row 1 = logo + account actions, row 2 = tabs.
							flexWrap: { xs: "wrap", md: "nowrap" },
							columnGap: 1,
						}}
					>
						<Box
							component="h1"
							sx={{
								order: 1,
								flexGrow: 1,
								m: 0,
								minWidth: 0,
								display: "flex",
								justifyContent: "flex-start",
							}}
						>
							<Box
								component="img"
								src="/price-peer-logo-horizontal.svg"
								alt="Price Peer"
								sx={{ height: { xs: 40, sm: 58, md: 68 }, width: "auto", maxWidth: "100%" }}
							/>
						</Box>
						{showTabs && (
							<Tabs
								value={mainTab}
								onChange={handleTabChange}
								textColor="primary"
								variant="scrollable"
								scrollButtons={false}
								allowScrollButtonsMobile
								sx={{
									// Phones use the bottom navigation bar instead.
									display: { xs: "none", sm: "flex" },
									order: { xs: 3, md: 2 },
									ml: { xs: 0, md: 2 },
									mt: { xs: 0.5, md: 0 },
									minHeight: "auto",
									maxWidth: "100%",
									width: { xs: "100%", md: "auto" },
									"& .MuiTabs-flexContainer": {
										justifyContent: { xs: "center", md: "flex-start" },
									},
									"& .MuiTab-root": {
										minWidth: "auto",
										px: { xs: 1.25, md: 2 },
										minHeight: { xs: 44, md: "auto" },
										fontSize: { xs: 13, sm: 13, md: 14 },
										color: "text.secondary",
									},
									"& .MuiTab-root.Mui-selected": {
										color: "#6e7bff",
									},
									"& .MuiTabs-indicator": {
										backgroundColor: "#6e7bff",
									},
								}}
							>
								<Tab label="Products" value="products" />
								<Tab label="My Receipts" value="receipts" />
								<Tab label="My Favorites" value="favorites" />
								{isAuthenticated && isAdmin && (
									<Tab label="Admin" value="admin" />
								)}
							</Tabs>
						)}
						<Box
							sx={{
								order: { xs: 2, md: 3 },
								display: "flex",
								alignItems: "center",
								justifyContent: "flex-end",
								flexShrink: 0,
								gap: 1,
								ml: { md: 2 },
							}}
						>
							{isAuthenticated && userEmail && (
								<Typography
									variant="body2"
									sx={{
										// Too wide for the phone header row; the Log out button is enough there.
										display: { xs: "none", sm: "block" },
										fontWeight: 500,
										color: "text.secondary",
										maxWidth: { xs: 180, sm: 200 },
										overflow: "hidden",
										textOverflow: "ellipsis",
										whiteSpace: "nowrap",
									}}
								>
									{userEmail}
								</Typography>
							)}
							{isGuest && (
								<Typography
									variant="body2"
									sx={{
										display: { xs: "none", sm: "block" },
										fontWeight: 500,
										color: "text.secondary",
									}}
								>
									Guest access
								</Typography>
							)}
							{isAuthenticated && (
								<Button
									onClick={handleLogout}
									sx={{ fontWeight: 500, color: "text.secondary" }}
								>
									Log out
								</Button>
							)}
							{(isGuest || (!isAuthenticated && !auth.isLoading)) && (
								<Button
									variant="contained"
									onClick={() => auth.signinRedirect()}
									sx={{
										fontWeight: 500,
										minHeight: { xs: 40, md: "auto" },
										background: "linear-gradient(135deg, #6e7bff, #8b6eff)",
										"&:hover": { background: "linear-gradient(135deg, #6e7bff, #8b6eff)", filter: "brightness(1.06)" },
									}}
								>
									Sign in
								</Button>
							)}
						</Box>
					</Container>
				</Toolbar>
			</AppBar>

			<Container
				component="main"
				maxWidth="lg"
				sx={{
					py: { xs: 2, md: 3 },
					// Keep the last row clear of the fixed bottom navigation on phones.
					pb: showTabs
						? {
								xs: `calc(${BOTTOM_NAV_HEIGHT + 16}px + env(safe-area-inset-bottom))`,
								md: 3,
							}
						: { xs: 2, md: 3 },
				}}
			>
				{renderMainContent()}
			</Container>

			{showTabs && (
				<Paper
					component="nav"
					aria-label="Main navigation"
					elevation={0}
					sx={{
						display: { xs: "block", sm: "none" },
						position: "fixed",
						left: 0,
						right: 0,
						bottom: 0,
						zIndex: (t) => t.zIndex.appBar,
						borderRadius: 0,
						background: "rgba(255,255,255,0.94)",
						backdropFilter: "blur(30px) saturate(180%)",
						WebkitBackdropFilter: "blur(30px) saturate(180%)",
						borderTop: "0.5px solid rgba(120,120,128,0.25)",
						pb: "env(safe-area-inset-bottom)",
					}}
				>
					<BottomNavigation
						showLabels
						value={mainTab}
						onChange={handleTabChange}
						sx={{
							height: BOTTOM_NAV_HEIGHT,
							bgcolor: "transparent",
							"& .MuiBottomNavigationAction-root": {
								minWidth: 0,
								color: "text.secondary",
							},
							"& .MuiBottomNavigationAction-root.Mui-selected": {
								color: "#6e7bff",
							},
							"& .MuiBottomNavigationAction-label": { fontSize: 11 },
							"& .MuiBottomNavigationAction-label.Mui-selected": { fontSize: 11 },
						}}
					>
						<BottomNavigationAction label="Products" value="products" icon={<StorefrontIcon />} />
						<BottomNavigationAction label="Receipts" value="receipts" icon={<ReceiptLongIcon />} />
						<BottomNavigationAction label="Favorites" value="favorites" icon={<FavoriteIcon />} />
						{isAuthenticated && isAdmin && (
							<BottomNavigationAction label="Admin" value="admin" icon={<AdminPanelSettingsIcon />} />
						)}
					</BottomNavigation>
				</Paper>
			)}

			<Dialog
				open={restrictedDialogOpen}
				onClose={handleCloseRestrictedDialog}
				aria-labelledby="signin-required-title"
				aria-describedby="signin-required-description"
			>
				<DialogTitle id="signin-required-title">Sign in required</DialogTitle>
				<DialogContent>
					<DialogContentText id="signin-required-description">
						{restrictedTarget} is only available after signing in. Guests can
						view products only.
					</DialogContentText>
				</DialogContent>
				<DialogActions>
					<Button onClick={handleCloseRestrictedDialog}>Keep browsing</Button>
					<Button variant="contained" onClick={handleRestrictedSignIn}>
						Sign in
					</Button>
				</DialogActions>
			</Dialog>
		</Box>
	);
}

export default App;
