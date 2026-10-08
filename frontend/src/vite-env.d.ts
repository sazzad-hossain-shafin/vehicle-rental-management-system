/// <reference types="vite/client" />

interface ImportMetaEnv {
  /** Base path of the API on this origin. Defaults to "/api/v1". Not a secret: it is public by nature. */
  readonly VITE_API_BASE_PATH?: string
}

interface ImportMeta {
  readonly env: ImportMetaEnv
}
