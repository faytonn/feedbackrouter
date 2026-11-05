import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import './styles/tokens.css'
import './styles/base.css'
import { ComponentSheet } from './dev/ComponentSheet'

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <ComponentSheet />
  </StrictMode>,
)
