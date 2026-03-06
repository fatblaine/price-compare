import { useCallback } from "react";
import { driver } from "driver.js";

const TOUR_SEEN_KEY = "tourSeen";

function createTourDriver() {
	return driver({
		showProgress: true,
		allowClose: true,
		steps: [
			{
				element: "#search-input",
				popover: {
					title: "Search Products",
					description: "Type a product name to search, e.g. 'milk'",
				},
			},
			{
				element: "#shop-filter",
				popover: {
					title: "Filter by Shop",
					description: "Filter by supermarket: Woolworths or Coles",
				},
			},
			{
				element: ".product-card",
				popover: {
					title: "Product Card",
					description: "Each card shows the current price and pack size",
				},
			},
			{
				element: ".compare-btn",
				popover: {
					title: "Compare Prices",
					description:
						"Click Compare to see the same product's price at other supermarkets",
				},
			},
			{
				element: ".favorite-btn",
				popover: {
					title: "Save Favourites",
					description:
						"Log in to save frequently purchased products to your favourites",
				},
			},
		],
		onDestroyStarted: (element, step, options) => {
			localStorage.setItem(TOUR_SEEN_KEY, "1");
			options.driver.destroy();
		},
	});
}

export function useTour() {
	const startTour = useCallback(() => {
		const d = createTourDriver();
		d.drive();
	}, []);

	const autoStart = useCallback(() => {
		if (localStorage.getItem(TOUR_SEEN_KEY) !== "1") {
			const d = createTourDriver();
			d.drive();
		}
	}, []);

	return { startTour, autoStart };
}
