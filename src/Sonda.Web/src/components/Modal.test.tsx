import { fireEvent, render, screen } from '@testing-library/react';
import { expect, it, vi } from 'vitest';
import { Modal } from './Modal';

it('closes without routing, restores scroll and returns focus to Info', () => {
  HTMLDialogElement.prototype.showModal = function () { this.setAttribute('open', ''); };
  HTMLDialogElement.prototype.close = function () { this.removeAttribute('open'); };
  const scroll = vi.spyOn(window, 'scrollTo').mockImplementation(() => {});
  const opener = document.createElement('button'); opener.textContent = 'Info'; document.body.append(opener); opener.focus();
  const close = vi.fn();
  const view = render(<Modal title="Incident" onClose={close}><p>Overview</p></Modal>);
  expect(screen.getByRole('dialog', { name: 'Incident' })).toBeVisible();
  expect(document.body.style.overflow).toBe('hidden');
  fireEvent.click(screen.getByRole('button', { name: 'Close Incident' }));
  expect(close).toHaveBeenCalledOnce();
  view.unmount();
  expect(opener).toHaveFocus(); expect(document.body.style.overflow).toBe(''); expect(scroll).toHaveBeenCalledWith(0, 0);
  opener.remove(); scroll.mockRestore();
});
