# GCN-25 Medicine Catalogue Browsing, Search and Filtering

## Purpose

Signed-in Patient users need a catalogue where they can find available pharmacy medicines before selecting one in GCN-26.

## Scope

The catalogue is a Patient-only feature. It lists active medicines and supports name or description search, category filtering, and server-side pagination with 12 medicines per page.

## API

Add a customer catalogue endpoint separate from staff medicine management:

`GET /api/catalogue/medicines?search=&categoryId=&page=1&pageSize=12`

The endpoint is authorized for the Patient role. It returns only active medicines and supplies each item with:

- medicine ID, name, description and price;
- category ID and category name;
- image URL;
- whether a prescription is required;
- stock quantity and availability state.

The response also contains the current page, page size, total matching medicine count, and total page count. The server always uses a maximum page size of 12.

`GET /api/catalogue/categories` returns categories that have at least one active medicine, for the filter dropdown.

## Customer screen

Add `/patient/medicines` to the Patient sidebar. It uses the existing CareNova red, pink and white dashboard theme.

The page contains a search field, category dropdown, medicine-card grid, and Previous/Next page controls. The page requests the catalogue when a patient searches, changes category, or selects a page.

Every card shows the medicine image when available, name, category, LKR price, prescription requirement, and availability. A medicine with zero stock remains visible and shows `Out of stock`; its View details button is disabled. Available medicines show View details, which will become the GCN-26 navigation point.

## Errors and empty results

An empty matching result shows a clear message. API failures show a retry message while retaining the current filter controls.

## Verification

Cover catalogue search, category filtering, page totals, active-medicine filtering, and out-of-stock representation with backend tests. Build the frontend and confirm the Patient page permits a Patient user and blocks other roles.
