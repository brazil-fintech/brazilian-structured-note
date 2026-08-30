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

export const api = {
  listFigures: (includeDisabled = false) =>
    request<FigureSummary[]>(`/figures?includeDisabled=${includeDisabled}`),

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

  reference: (source: string, assetClass?: string) =>
    request<ReferenceItem[]>(`/reference/${source}${assetClass ? `?assetClass=${encodeURIComponent(assetClass)}` : ''}`),
};

export type { Json };
