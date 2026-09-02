import type { FigureTemplate, InstanceValues, Json, ValidationMessage } from '../engine/types';

const BASE = (import.meta.env.VITE_API_BASE_URL as string | undefined) ?? '/api';

export interface FigureSummary {
  code: string;
  name: string;
  commercialName?: string;
  description?: string;
  modalities: string[];
  status: string;
  templateVersion?: number;
}

/** A figure of B3's catalogue, whether or not this platform can book it. */
export interface FigureCatalogueEntry {
  code: string;
  name: string;
  b3Name?: string;
  commercialName?: string;
  description?: string;
  modalities: string[];
  availability: 'Available' | 'Pending' | 'Quarantined' | 'Retired' | 'NotConfigured';
  bookable: boolean;
  templateVersion?: number;
  calculatedByB3: boolean;
  inB3Catalogue: boolean;
  lastError?: string;
}

export interface FigureCoverage {
  published: number;
  configured: number;
  bookable: number;
}

export interface FigureCatalogueResponse {
  figures: FigureCatalogueEntry[];
  coverage: FigureCoverage;
}

export interface AssetListItem {
  id: string;
  figureCode: string;
  figureName?: string;
  commercialName: string;
  instrumentCode?: string;
  isinCode?: string;
  issueDate: string;
  maturityDate: string;
  modality?: string;
  underlyingClass?: string;
  underlying?: string;
  notionalAmount?: number;
  status: string;
  updatedUtc: string;
}

export interface AssetListResponse {
  items: AssetListItem[];
  total: number;
  page: number;
  pageSize: number;
}

export interface AssetDetail {
  id: string;
  figureCode: string;
  templateVersion: number;
  status: string;
  values: InstanceValues;
  rowVersion?: string;
  createdUtc: string;
  createdBy?: string;
  updatedUtc: string;
  updatedBy?: string;
}

export interface ValidateResponse {
  messages: ValidationMessage[];
  evaluatedPaths: string[];
  isValid: boolean;
}

export interface SaveAssetResponse {
  saved: boolean;
  assetId?: string;
  rowVersion?: string;
  messages: ValidationMessage[];
  conflict?: string;
}

/** One CETIP upload file, as `GET /api/assets/{id}/clearing` returns it. */
export interface ClearingFile {
  /** The section of the ENVIAR ARQUIVOS manual the layout comes from, e.g. "4.8.1 Registro COE". */
  layout: string;
  /** The operation code the header carries, e.g. `0001` or `FLUX`; it also names the download route. */
  operation: string;
  fileName: string;
  /** Lines after the header. */
  records: number;
  /** The whole file, CRLF-terminated. */
  content: string;
}

export interface ClearingResponse {
  files: ClearingFile[];
  /** What went into the files, and what could not — an attribute B3 registers that went out blank. */
  notes: string[];
}

/** The optional inputs of a generation: neither is a property of the certificate. */
export interface ClearingParams {
  /** Overrides the issuer short name configured server-side (Clearing:ParticipantName). */
  participant?: string;
  /** The date the upload is stamped with, ISO. Defaults server-side to today. */
  date?: string;
}

/** Built here rather than inline so both the JSON and the bytes route agree on the query. */
export function clearingPath(assetId: string, params: ClearingParams = {}, operation?: string): string {
  const query = new URLSearchParams();
  if (params.participant?.trim()) query.set('participant', params.participant.trim());
  if (params.date) query.set('date', params.date);
  const suffix = query.toString();
  const base = `/assets/${encodeURIComponent(assetId)}/clearing`;
  const path = operation ? `${base}/${encodeURIComponent(operation)}` : base;
  return suffix ? `${path}?${suffix}` : path;
}

export interface ReferenceItem {
  code: string;
  name: string;
  group?: string;
}

export class ApiError extends Error {
  constructor(readonly status: number, message: string, readonly body?: unknown) {
    super(message);
  }
}

async function request<T>(path: string, init?: RequestInit): Promise<T> {
  const response = await fetch(`${BASE}${path}`, {
    ...init,
    headers: { 'Content-Type': 'application/json', ...(init?.headers ?? {}) },
  });

  const text = await response.text();
  const body = text ? (JSON.parse(text) as unknown) : null;

  if (!response.ok) {
    // 409 and 422 carry a normal payload: a conflict, or the messages that blocked the save.
    if (response.status === 409 || response.status === 422) return body as T;
    const message = (body as { message?: string } | null)?.message ?? response.statusText;
    throw new ApiError(response.status, message, body);
  }

  return body as T;
}

/**
 * What a failed byte response has to say. The API words its refusals as `{ message }`, but a
 * proxy in front of it may answer with an HTML page, which is not worth a parse error of its own.
 */
async function failureMessage(response: Response): Promise<string> {
  try {
    const text = await response.text();
    return (text ? (JSON.parse(text) as { message?: string }).message : null) ?? response.statusText;
  } catch {
    return response.statusText;
  }
}

export const api = {
  listFigures: (includeDisabled = false) =>
    request<FigureSummary[]>(`/figures?includeDisabled=${includeDisabled}`),

  listFigureCatalogue: () => request<FigureCatalogueResponse>('/figures/catalogue'),

  getTemplate: (figureCode: string, version?: number) =>
    request<FigureTemplate>(`/figures/${encodeURIComponent(figureCode)}/template${version ? `?version=${version}` : ''}`),

  listAssets: (params: {
    referenceDate: string;
    figureCode?: string;
    modality?: string;
    search?: string;
    page?: number;
    pageSize?: number;
  }) => {
    const query = new URLSearchParams({ referenceDate: params.referenceDate });
    if (params.figureCode) query.set('figureCode', params.figureCode);
    if (params.modality) query.set('modality', params.modality);
    if (params.search) query.set('search', params.search);
    if (params.page) query.set('page', String(params.page));
    if (params.pageSize) query.set('pageSize', String(params.pageSize));
    return request<AssetListResponse>(`/assets?${query.toString()}`);
  },

  getAsset: (id: string) => request<AssetDetail>(`/assets/${id}`),

  /** As-you-type validation. `changedPaths` narrows the pass to what the user just touched. */
  validate: (body: {
    figureCode: string;
    values: InstanceValues;
    changedPaths?: string[];
    assetId?: string;
    scope?: 'field' | 'form' | 'submit';
    culture?: string;
  }, signal?: AbortSignal) =>
    request<ValidateResponse>('/assets/validate', {
      method: 'POST',
      body: JSON.stringify(body),
      signal,
    }),

  saveAsset: (id: string | null, body: {
    figureCode: string;
    values: InstanceValues;
    rowVersion?: string;
    acceptWarnings?: boolean;
    culture?: string;
  }) =>
    request<SaveAssetResponse>(id ? `/assets/${id}` : '/assets', {
      method: id ? 'PUT' : 'POST',
      body: JSON.stringify(body),
    }),

  /**
   * The CETIP upload files a booked asset produces. Generated on demand from the stored values,
   * so nothing is cached that could go stale behind an edit.
   */
  clearingFiles: (assetId: string, params: ClearingParams = {}) =>
    request<ClearingResponse>(clearingPath(assetId, params)),

  /**
   * One file as bytes. The API encodes it single-byte, as CETIP reads it, so the download goes
   * through fetch rather than a plain link: re-encoding the JSON preview in the browser would
   * turn every "ç" of a commercial name into two characters and shift the layout after it.
   */
  clearingFileBlob: async (assetId: string, operation: string, params: ClearingParams = {}) => {
    const response = await fetch(`${BASE}${clearingPath(assetId, params, operation)}`);
    if (!response.ok) throw new ApiError(response.status, await failureMessage(response));
    return response.blob();
  },

  reference: (source: string, assetClass?: string) =>
    request<ReferenceItem[]>(`/reference/${source}${assetClass ? `?assetClass=${encodeURIComponent(assetClass)}` : ''}`),
};

export type { Json };
