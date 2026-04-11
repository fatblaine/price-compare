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
				element: "#ai-search-btn",
				popover: {
					title: "AI Search",
					description:
						"Can't find what you're looking for? Click here to describe it in any language — our AI will figure out the product for you. e.g. '澳大利亚最有名的饼干' or 'something sweet to put on toast'.",
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
				element: ".recommendation-text",
				popover: {
					title: "Purchase Recommendation",
					description:
						"When a matching product is found at another store, we show which shop is cheaper and how much you can save.",
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
