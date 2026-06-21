import IMask from '../core/holder.js';

/**  Generic element API to use with mask */
class MaskElement {
    /** */

    /** */

    /** */

    /** Safely returns selection start */
    get selectionStart() {
        let start;
        try {
            start = this._unsafeSelectionStart;
        } catch {
        }
        return start != null ? start : this.value.length;
    }

    /** Safely returns selection end */
    get selectionEnd() {
        let end;
        try {
            end = this._unsafeSelectionEnd;
        } catch {
        }
        return end != null ? end : this.value.length;
    }

    /** */
    get isActive() {
        return false;
    }

    /** Safely sets element selection */
    select(start, end) {
        if (start == null || end == null || start === this.selectionStart && end === this.selectionEnd) return;
        try {
            this._unsafeSelect(start, end);
        } catch {
        }
    }

    /** */

    /** */

    /** */
}

IMask.MaskElement = MaskElement;

export {MaskElement as default};
