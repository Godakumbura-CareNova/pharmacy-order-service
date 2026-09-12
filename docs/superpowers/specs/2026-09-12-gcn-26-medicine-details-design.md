# GCN-26 Medicine Details and Quantity Selection

## Scope

Signed-in Patient users can open an active, in-stock medicine from the GCN-25 catalogue and select a quantity from one up to the current stock quantity.

## API

Add `GET /api/catalogue/medicines/{id}` with Patient authorization. It returns the active medicine's image, name, category, description, price, prescription requirement, and stock quantity. Missing, inactive, or zero-stock medicines return an unavailable response.

## Customer screen

Add `/patient/medicines/:medicineId`. Show medicine details, a minus/plus quantity selector, available stock, and a calculated LKR total. Quantity begins at one and is constrained to the available stock.

For a non-prescription medicine, show `Add to Cart`. For a prescription medicine, show `Continue to prescription upload`. These actions preserve the selected medicine and quantity for GCN-29; they do not create a cart in this story.

## Verification

Test active medicine lookup and unavailable handling on the API. Test the quantity bounds, total calculation, and prescription action label in the frontend. Build the frontend and confirm the Patient route works.
