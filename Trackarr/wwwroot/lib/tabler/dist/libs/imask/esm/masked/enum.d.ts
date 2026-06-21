import MaskedPattern, {type MaskedPatternOptions, MaskedPatternState} from './pattern';
import {AppendFlags} from './base';
import ChangeDetails from '../core/change-details';
import {TailDetails} from '../core/tail-details';

export type MaskedEnumOptions =
    Omit<MaskedPatternOptions, 'mask'>
    & Pick<MaskedEnum, 'enum'>
    & Partial<Pick<MaskedEnum, 'matchValue'>>;
export type MaskedEnumPatternOptions = MaskedPatternOptions & Partial<Pick<MaskedEnum, 'enum' | 'matchValue'>>;
/** Pattern which validates enum values */
export default class MaskedEnum extends MaskedPattern {
    static DEFAULTS: typeof MaskedPattern.DEFAULTS & Pick<MaskedEnum, 'matchValue'>;
    enum: Array<string>;
    /** Match enum value */
    matchValue: (enumStr: string, inputStr: string, matchFrom: number) => boolean;

    constructor(opts?: MaskedEnumOptions);

    get isComplete(): boolean;

    updateOptions(opts: Partial<MaskedEnumOptions>): void;

    _update(opts: Partial<MaskedEnumOptions>): void;

    _appendCharRaw(ch: string, flags?: AppendFlags<MaskedPatternState>): ChangeDetails;

    extractTail(fromPos?: number, toPos?: number): TailDetails;

    remove(fromPos?: number, toPos?: number): ChangeDetails;
}
//# sourceMappingURL=enum.d.ts.map