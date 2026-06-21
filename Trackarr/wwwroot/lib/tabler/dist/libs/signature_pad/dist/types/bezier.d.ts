import {BasicPoint, Point} from './point.js';

export declare class Bezier {
    private static calculateControlPoints;
    startPoint: Point;
    control2: BasicPoint;
    control1: BasicPoint;
    endPoint: Point;
    startWidth: number;
    endWidth: number;
    private point;

    constructor(startPoint: Point, control2: BasicPoint, control1: BasicPoint, endPoint: Point, startWidth: number, endWidth: number);

    static fromPoints(points: Point[], widths: {
        start: number;
        end: number;
    }): Bezier;

    length(): number;
}
