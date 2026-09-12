# GCN-29 Prescription Upload Design

## Scope

GCN-29 lets a Patient upload one prescription for one selected prescription-required medicine and quantity. It records the upload as pending verification and gives the patient a private prescription history. Cart creation remains in GCN-30 and staff verification remains in GCN-32.

## Patient flow

1. On the GCN-26 medicine detail page, the patient selects a quantity for an in-stock prescription-required medicine and chooses **Continue to prescription upload**.
2. The frontend reads the existing `carenova.pendingCartItem` session value and opens `/patient/prescriptions/upload`.
3. The upload page shows the medicine name and selected quantity, accepts one PDF, JPG, JPEG, or PNG file up to 10 MB, and provides an **Upload prescription** action.
4. On success, the patient sees a confirmation containing the original file name, selected medicine, quantity, upload time, and **Pending verification** status. A **Continue shopping** action returns to the catalogue.
5. `/patient/prescriptions` lists only the signed-in patient's uploaded prescriptions. Each row shows the original file name, medicine, quantity, uploaded time, and current status. The patient can open their own file.

## Data model

The existing `Prescription` entity is extended with:

- `OriginalFileName` — safe display name of the uploaded file.
- `MedicineId` — selected medicine.
- `Quantity` — selected quantity.
- `Status` — initially `PendingVerification`; GCN-32 can later change it to `Verified` or `Rejected`.

`UploadedBy` remains the authenticated Patient ID. The record does not create a cart item and has no Kafka event in this story.

## File storage and access

The API accepts only PDF, JPG, JPEG, and PNG files, limited to 10 MB. It generates a unique storage filename and saves the file under `wwwroot/uploads/prescriptions`. The database stores a safe relative file path, never a client-supplied absolute path.

Prescription files are not exposed through anonymous static-file URLs. A Patient-authorized download endpoint checks that the record belongs to the authenticated patient before returning the file. A PharmacyStaff endpoint is deferred to GCN-32.

## API

- `POST /api/prescriptions` — Patient-only multipart upload. It receives `medicineId`, `quantity`, and one file. It validates the current medicine is active, prescription-required, in stock, and that quantity is between one and current stock.
- `GET /api/prescriptions` — Patient-only list of the caller's prescription records.
- `GET /api/prescriptions/{id}/file` — Patient-only download for the caller's own uploaded file.

Responses include the record ID, original file name, medicine ID and name, quantity, status, and uploaded time.

## Frontend

Add `prescriptionApi` calls, an upload page, a confirmation view, and a `My prescriptions` page. Add an enabled **Prescriptions** navigation item for Patients. The GCN-26 prescription action navigates to the upload page while retaining the selected medicine and quantity in session storage. Non-prescription medicines keep their existing GCN-30 handoff behavior.

## Errors

The upload page explains invalid file type, file size, missing selection, unavailable medicine, and invalid quantity errors without losing the file-selection form. The history page has loading, empty, and retry states. A patient receives a generic not-found response for another patient's prescription ID.

## Validation

Backend tests cover accepted upload metadata, rejected file types, over-limit files, medicine and quantity validation, and patient ownership for lists and downloads. Frontend tests cover the selected medicine summary, accepted-file guard, successful upload confirmation, and private-history rendering. The API and frontend production build are run before the branch is pushed.
