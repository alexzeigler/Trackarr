import type {AppendTail, TailDetails} from '../../core/tail-details';
import ChangeDetails from '../../core/change-details';
import type MaskedPattern from '../pattern';

export type ChunksTailState = Pick<ChunksTailDetails, 'from' | 'stop' | 'blockIndex'> & {
    chunks: Array<TailDetails['state']>;
};
export default class ChunksTailDetails implements TailDetails {
    chunks: Array<TailDetails>;
    from: number;
    stop?: number;
    /** */
    blockIndex?: number;

    constructor(chunks?: Array<TailDetails>, from?: number);

    get state(): ChunksTailState;

    set state(state: ChunksTailState);

    toString(): string;

    extend(tailChunk: string | String | TailDetails): void;

    appendTo(masked: AppendTail | MaskedPattern): ChangeDetails;

    unshift(beforePos?: number): string;

    shift(): string;
}
//# sourceMappingURL=chunk-tail-details.d.ts.map